using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoiceType.Infrastructure.Config;
using VoiceType.Infrastructure.Logging;
using VoiceType.Models;

namespace VoiceType.Infrastructure.Whisper
{
    // Manages a whisper-stream.exe process and streams raw PCM16 LE to its stdin.
    // NOTE: whisper-stream.exe command-line must accept "-f -" to read from stdin; the exact args
    // can be customized via VoiceTypeSettings.WhisperStreamArguments.
    public class WhisperStreamClient : IDisposable
    {
        private readonly VoiceTypeSettings _settings;
        private Process? _proc;
        private readonly object _writeLock = new object();
        private readonly StringBuilder _stdoutBuffer = new StringBuilder();
        private readonly StringBuilder _stderrBuffer = new StringBuilder();
        private Task? _stdoutReaderTask;
        private Task? _stderrReaderTask;
        private CancellationTokenSource? _readersCts;
        private TerminalEmulator? _terminal;

        // Note: avoid P/Invoke for console control here to keep simpler build/run behavior.
        // We'll attempt a polite CloseMainWindow() and then fall back to Kill(true) if needed.

        // Raised for each line of stdout produced by the whisper-stream process.
        public event Action<string>? OutputLineReceived;
        // Raised for each line of stderr produced by the whisper-stream process.
        public event Action<string>? ErrorLineReceived;

        public WhisperStreamClient(VoiceTypeSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        private static async Task<string?> ReadLineWithCancellation(StreamReader sr, CancellationToken ct)
        {
            // ReadLineAsync has no cancellation token; emulate by polling Task.WhenAny
            var readTask = sr.ReadLineAsync();
            var tcs = new TaskCompletionSource<bool>();
            using (ct.Register(() => tcs.TrySetResult(true)))
            {
                var completed = await Task.WhenAny(readTask, tcs.Task).ConfigureAwait(false);
                if (completed == tcs.Task)
                {
                    return null;
                }
                return await readTask.ConfigureAwait(false);
            }
        }

        // Attempt a graceful stop for mic-mode processes: send CTRL+C to the process console, wait for exit, then fallback to kill.
        public async Task<FinalTranscriptionResult> StopAndCollectAsync(int timeoutMs = 1000)
        {
            if (_proc == null) return new FinalTranscriptionResult { Success = false, ErrorMessage = "Process not started" };

            try
            {
                var pid = _proc.Id;
                var exited = false;

                try
                {
                    // First attempt a polite close: try CloseMainWindow for GUI-based wrappers
                    try
                    {
                        if (_proc.CloseMainWindow())
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            while (sw.ElapsedMilliseconds < timeoutMs && !_proc.HasExited)
                            {
                                await Task.Delay(50).ConfigureAwait(false);
                            }
                            sw.Stop();
                        }
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error attempting graceful stop: {ex.Message}");
                }

                if (!_proc.HasExited)
                {
                    try
                    {
                        // fallback: kill the process
                        // Signal reader tasks to stop while we shut down the process
                        try { _readersCts?.Cancel(); } catch { }
                        _proc.Kill(true);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to kill whisper-stream process: {ex.Message}");
                    }
                }

                try
                {
                    await _proc.WaitForExitAsync();
                }
                catch { }

                string stdout, stderr;
                lock (_stdoutBuffer) { stdout = _stdoutBuffer.ToString(); }
                lock (_stderrBuffer) { stderr = _stderrBuffer.ToString(); }

                var exit = 0;
                try { exit = _proc.ExitCode; } catch { }

                return new FinalTranscriptionResult { Success = true, Text = stdout?.Trim() ?? string.Empty, ExitCode = exit };
            }
            catch (Exception ex)
            {
                return new FinalTranscriptionResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                try { _proc.Dispose(); } catch { }
                _proc = null;
                try { _readersCts?.Cancel(); } catch { }
                try { _readersCts?.Dispose(); } catch { }
                _readersCts = null;
            }
        }

        public bool IsRunning => _proc != null && !_proc.HasExited;

        // Expose process for reading stdout in mic mode and other process-level operations
        public Process? GetProcess() => _proc;

        public bool Start(out string? startError)
        {
            startError = null;

            var runner = new WhisperProcessRunner(_settings);
            var probe = runner.ProbePaths();
            var exe = probe.ExecutablePath;
            var model = probe.ModelPath ?? _settings.WhisperModelPath ?? string.Empty;

            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                startError = "Whisper stream executable not found.";
                return false;
            }

            var args = _settings.WhisperStreamArguments ?? string.Empty;
            // Ensure we instruct whisper-stream to read from stdin using -f - (common convention)
            if (!args.Contains("-f") && !args.Contains("--file"))
            {
                args = (args + " -f -").Trim();
            }

            // Ensure model argument is present
            if (!string.IsNullOrWhiteSpace(model) && !args.Contains("-m") && !args.Contains("--model"))
            {
                args = $"-m \"{model}\" " + args;
            }

            if (!args.Contains("-l") && !string.IsNullOrWhiteSpace(_settings.Language))
            {
                args = args + " -l " + _settings.Language;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                // Ensure working directory is the executable directory so relative model paths resolve correctly
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            try
            {
                Logger.Info($"Starting whisper-stream: {exe} {psi.Arguments}");
                _proc = Process.Start(psi);
                if (_proc == null)
                {
                    startError = "Failed to start whisper-stream process.";
                    Logger.Error(startError);
                    return false;
                }

                // terminal emulator instance to process fragments incrementally
                _terminal = new TerminalEmulator();

                // start background reader for stderr to capture runtime errors
                _readersCts = new CancellationTokenSource();
                var token = _readersCts.Token;
                _stderrReaderTask = Task.Run(async () =>
                {
                    try
                    {
                        var procRef = _proc;
                        if (procRef == null) return;
                        var sr = procRef.StandardError;
                        while (!procRef.HasExited && !token.IsCancellationRequested)
                        {
                            var line = await ReadLineWithCancellation(sr, token);
                            if (line == null) break;
                            Logger.Error($"whisper-stream stderr: {line}");
                            lock (_stderrBuffer) { if (_stderrBuffer.Length > 0) _stderrBuffer.AppendLine(); _stderrBuffer.Append(line.Trim()); }
                            ErrorLineReceived?.Invoke(line.Trim());
                        }
                        // drain any remaining
                        var rest = await sr.ReadToEndAsync();
                        if (!string.IsNullOrWhiteSpace(rest))
                        {
                            Logger.Error($"whisper-stream stderr: {rest}");
                            lock (_stderrBuffer) { if (_stderrBuffer.Length > 0) _stderrBuffer.AppendLine(); _stderrBuffer.Append(rest.Trim()); }
                            foreach (var errLine in rest.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                ErrorLineReceived?.Invoke(errLine.Trim());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error reading whisper-stream stderr: {ex.Message}");
                    }
                });
                // start background reader for stdout to supply live transcripts (read raw bytes to preserve partial updates)
                _stdoutReaderTask = Task.Run(async () => await StdoutBaseStreamReaderAsync(_proc!, token), token);

                Logger.Info("whisper-stream started");
                return true;
            }
            catch (Exception ex)
            {
                startError = ex.Message;
                Logger.Error($"Failed to start whisper-stream: {ex.Message}");
                return false;
            }
        }

        // Start whisper-stream.exe in mic mode (no stdin, no -f/-file argument)
        public bool StartMicMode(out string? startError)
        {
            startError = null;

            var runner = new WhisperProcessRunner(_settings);
            var probe = runner.ProbePaths();
            var exe = probe.ExecutablePath;
            var model = probe.ModelPath ?? _settings.WhisperModelPath ?? string.Empty;

            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                startError = "Whisper stream executable not found.";
                return false;
            }

            var args = _settings.WhisperStreamArguments ?? string.Empty;
            // Remove any -f/-file argument for mic mode
            args = System.Text.RegularExpressions.Regex.Replace(args, @"-f\s+[^\s]+", "");
            args = System.Text.RegularExpressions.Regex.Replace(args, @"--file\s+[^\s]+", "");

            // Ensure model argument is present
            if (!string.IsNullOrWhiteSpace(model) && !args.Contains("-m") && !args.Contains("--model"))
            {
                args = $"-m \"{model}\" " + args;
            }

            if (!args.Contains("-l") && !string.IsNullOrWhiteSpace(_settings.Language))
            {
                args = args + " -l " + _settings.Language;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args.Trim(),
                // Ensure working directory is the executable directory so relative model paths resolve correctly
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            try
            {
                Logger.Info($"Starting whisper-stream (mic mode): {exe} {psi.Arguments}");
                _proc = Process.Start(psi);
                if (_proc == null)
                {
                    startError = "Failed to start whisper-stream process.";
                    Logger.Error(startError);
                    return false;
                }

                // prepare and start background readers with cancellation support
                try { _readersCts?.Cancel(); } catch { }
                try { _readersCts?.Dispose(); } catch { }
                _readersCts = new CancellationTokenSource();
                var token = _readersCts.Token;

                // start background reader for stderr to capture runtime errors
                _stderrReaderTask = Task.Run(async () =>
                {
                    try
                    {
                        var procRef = _proc;
                        if (procRef == null) return;
                        var sr = procRef.StandardError;
                        while (!procRef.HasExited && !token.IsCancellationRequested)
                        {
                            var line = await ReadLineWithCancellation(sr, token);
                            if (line == null) break;
                            Logger.Error($"whisper-stream stderr: {line}");
                            lock (_stderrBuffer) { if (_stderrBuffer.Length > 0) _stderrBuffer.AppendLine(); _stderrBuffer.Append(line.Trim()); }
                            ErrorLineReceived?.Invoke(line.Trim());
                        }
                        // drain any remaining
                        var rest = await sr.ReadToEndAsync();
                        if (!string.IsNullOrWhiteSpace(rest))
                        {
                            Logger.Error($"whisper-stream stderr: {rest}");
                            lock (_stderrBuffer) { if (_stderrBuffer.Length > 0) _stderrBuffer.AppendLine(); _stderrBuffer.Append(rest.Trim()); }
                            foreach (var errLine in rest.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                ErrorLineReceived?.Invoke(errLine.Trim());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error reading whisper-stream stderr: {ex.Message}");
                    }
                }, token);

                // start background reader for stdout to supply live transcripts
                _stdoutReaderTask = Task.Run(async () =>
                {
                    try
                    {
                        var procRef = _proc;
                        if (procRef == null) return;
                        var sr = procRef.StandardOutput;
                        while (!procRef.HasExited && !token.IsCancellationRequested)
                        {
                            var line = await ReadLineWithCancellation(sr, token);
                            if (line == null) break;
                            var trimmed = line.Trim();
                            lock (_stdoutBuffer)
                            {
                                if (_stdoutBuffer.Length > 0) _stdoutBuffer.AppendLine();
                                _stdoutBuffer.Append(trimmed);
                            }
                            OutputLineReceived?.Invoke(trimmed);
                        }
                        var rest = await sr.ReadToEndAsync();
                        if (!string.IsNullOrWhiteSpace(rest))
                        {
                            lock (_stdoutBuffer)
                            {
                                if (_stdoutBuffer.Length > 0) _stdoutBuffer.AppendLine();
                                _stdoutBuffer.Append(rest.Trim());
                            }
                            // send remaining lines
                            foreach (var outLine in rest.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                OutputLineReceived?.Invoke(outLine.Trim());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error reading whisper-stream stdout: {ex.Message}");
                    }
                }, token);

                Logger.Info("whisper-stream started (mic mode)");
                return true;
            }
            catch (Exception ex)
            {
                startError = ex.Message;
                Logger.Error($"Failed to start whisper-stream: {ex.Message}");
                return false;
            }
        }

        public void Write(byte[] buffer, int count)
        {
            if (_proc == null || _proc.HasExited) return;

            try
            {
                lock (_writeLock)
                {
                    var stdin = _proc.StandardInput.BaseStream;
                    stdin.Write(buffer, 0, count);
                    stdin.Flush();
                }
            }
            catch { }
        }

        public async Task<FinalTranscriptionResult> FinishAsync()
        {
            if (_proc == null) return new FinalTranscriptionResult { Success = false, ErrorMessage = "Process not started" };

            try
            {
                try
                {
                    // Closing StandardInput signals EOF to the child process, but only if redirected
                    if (_proc.StartInfo.RedirectStandardInput && _proc.StandardInput != null)
                    {
                        try { _proc.StandardInput.Close(); } catch { }
                    }
                }
                catch { }

                // Wait for process to exit and any reader tasks to finish
                await _proc.WaitForExitAsync();
                try { if (_stdoutReaderTask != null) await _stdoutReaderTask; } catch { }
                try { if (_stderrReaderTask != null) await _stderrReaderTask; } catch { }

                string stdout, stderr;
                lock (_stdoutBuffer) { stdout = _stdoutBuffer.ToString(); }
                lock (_stderrBuffer) { stderr = _stderrBuffer.ToString(); }
                var exit = _proc.ExitCode;

                if (exit != 0)
                {
                    return new FinalTranscriptionResult { Success = false, ErrorMessage = stderr, ExitCode = exit };
                }

                var text = stdout?.Trim() ?? string.Empty;
                return new FinalTranscriptionResult { Success = true, Text = text, ExitCode = exit };
            }
            catch (Exception ex)
            {
                return new FinalTranscriptionResult { Success = false, ErrorMessage = ex.Message };
            }
            finally
            {
                try { _proc.Dispose(); } catch { }
                _proc = null;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    try { _proc.Kill(true); } catch { }
                }
            }
            catch { }
            finally { _proc?.Dispose(); _proc = null; }
        }

        // Return current buffered stdout and stderr for diagnostics
        public (string Stdout, string Stderr) GetBufferedOutput()
        {
            lock (_stdoutBuffer)
            {
                lock (_stderrBuffer)
                {
                    return (_stdoutBuffer.ToString(), _stderrBuffer.ToString());
                }
            }
        }

        // Improved ANSI/VT renderer: maintains a line buffer and full cursor (row,col) to emulate overwrite/erase behavior
        // Supports: CR/LF, backspace, CSI sequences: K (erase line), J (erase display), A/B/C/D (cursor movements), H (CUP), G (column), s/u (save/restore), and ignores SGR (m).
        private static string RenderAnsiToVisible(string existingBuffer, string incoming)
        {
            if (string.IsNullOrEmpty(incoming)) return existingBuffer ?? string.Empty;
            // Initialize lines from existing buffer
            var lines = new System.Collections.Generic.List<StringBuilder>();
            if (!string.IsNullOrEmpty(existingBuffer))
            {
                var existingLines = existingBuffer.Split(new[] { '\n' });
                foreach (var l in existingLines)
                {
                    lines.Add(new StringBuilder(l));
                }
            }
            if (lines.Count == 0) lines.Add(new StringBuilder());

            int curLine = lines.Count - 1;
            int curCol = lines[curLine].Length;
            int savedLine = curLine, savedCol = curCol;

            int i = 0;
            while (i < incoming.Length)
            {
                var ch = incoming[i];
                if (ch == '\r')
                {
                    // Move cursor to start of current line
                    curCol = 0;
                    i++;
                    continue;
                }
                if (ch == '\n')
                {
                    // New line: advance
                    lines.Add(new StringBuilder());
                    curLine = lines.Count - 1;
                    curCol = 0;
                    i++;
                    continue;
                }
                if (ch == '\b')
                {
                    // Backspace: remove previous char if any
                    if (curCol > 0)
                    {
                        lines[curLine].Remove(curCol - 1, 1);
                        curCol--;
                    }
                    i++;
                    continue;
                }

                if (ch == '\u001b') // ESC
                {
                    // parse CSI sequences starting with ESC[
                    if (i + 1 < incoming.Length && incoming[i + 1] == '[')
                    {
                        var j = i + 2;
                        while (j < incoming.Length && (incoming[j] < '@' || incoming[j] > '~')) j++;
                        if (j < incoming.Length)
                        {
                            var cmd = incoming[j];
                            var args = incoming.Substring(i + 2, j - (i + 2));
                            var argList = args.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                            switch (cmd)
                            {
                                case 'K':
                                    // erase in line
                                    if (string.IsNullOrEmpty(args) || args == "0")
                                    {
                                        // remove from curCol to end
                                        if (curCol < lines[curLine].Length)
                                            lines[curLine].Remove(curCol, lines[curLine].Length - curCol);
                                    }
                                    else if (args == "1")
                                    {
                                        // remove from start to curCol
                                        if (curCol > 0)
                                        {
                                            lines[curLine].Remove(0, curCol);
                                            curCol = 0;
                                        }
                                    }
                                    else if (args == "2")
                                    {
                                        // clear entire line
                                        lines[curLine].Clear();
                                        curCol = 0;
                                    }
                                    break;
                                case 'J':
                                    // erase display
                                    if (string.IsNullOrEmpty(args) || args == "0")
                                    {
                                        // clear from cursor to end of screen
                                        // clear current line from curCol to end and following lines
                                        if (curCol < lines[curLine].Length)
                                            lines[curLine].Remove(curCol, lines[curLine].Length - curCol);
                                        for (int li = curLine + 1; li < lines.Count; li++) lines[li].Clear();
                                    }
                                    else if (args == "1")
                                    {
                                        // clear from start to cursor
                                        for (int li = 0; li < curLine; li++) lines[li].Clear();
                                        if (curCol > 0) lines[curLine].Remove(0, curCol);
                                        curCol = 0;
                                    }
                                    else if (args == "2")
                                    {
                                        // clear entire screen
                                        for (int li = 0; li < lines.Count; li++) lines[li].Clear();
                                        curLine = 0; curCol = 0;
                                    }
                                    break;
                                case 'A':
                                    // CUU: move up n
                                    {
                                        int n = 1; if (argList.Length > 0 && int.TryParse(argList[0], out var nn)) n = nn;
                                        curLine = Math.Max(0, curLine - n);
                                        curCol = Math.Min(curCol, lines[curLine].Length);
                                    }
                                    break;
                                case 'B':
                                    // CUD: move down n
                                    {
                                        int n = 1; if (argList.Length > 0 && int.TryParse(argList[0], out var nn)) n = nn;
                                        curLine = Math.Min(lines.Count - 1, curLine + n);
                                        curCol = Math.Min(curCol, lines[curLine].Length);
                                    }
                                    break;
                                case 'C':
                                    // CUF: forward
                                    {
                                        int n = 1; if (argList.Length > 0 && int.TryParse(argList[0], out var nn)) n = nn;
                                        curCol = Math.Min(lines[curLine].Length, curCol + n);
                                    }
                                    break;
                                case 'D':
                                    // CUB: backward
                                    {
                                        int n = 1; if (argList.Length > 0 && int.TryParse(argList[0], out var nn)) n = nn;
                                        curCol = Math.Max(0, curCol - n);
                                    }
                                    break;
                                case 'H':
                                case 'f':
                                    // CUP: move to row;col
                                    if (argList.Length >= 1 && int.TryParse(argList[0], out var r))
                                    {
                                        var c = 1;
                                        if (argList.Length >= 2 && int.TryParse(argList[1], out var cc)) c = cc;
                                        var targetLine = Math.Max(0, Math.Min(lines.Count - 1, r - 1));
                                        curLine = targetLine;
                                        curCol = Math.Max(0, Math.Min(lines[curLine].Length, c - 1));
                                    }
                                    break;
                                case 'G':
                                    // move cursor horizontal absolute
                                    if (argList.Length >= 1 && int.TryParse(argList[0], out var col)) curCol = Math.Max(0, col - 1);
                                    break;
                                case 's':
                                    // save cursor
                                    savedLine = curLine; savedCol = curCol;
                                    break;
                                case 'u':
                                    // restore cursor
                                    curLine = Math.Min(savedLine, lines.Count - 1); curCol = Math.Min(savedCol, lines[curLine].Length);
                                    break;
                                case 'm':
                                    // SGR - ignore
                                    break;
                                default:
                                    // unhandled
                                    break;
                            }
                            i = j + 1;
                            continue;
                        }
                    }
                    // if not CSI or incomplete, skip ESC
                    i++;
                    continue;
                }

                // Printable character: insert/overwrite at cursor
                var sb = lines[curLine];
                if (curCol >= sb.Length)
                {
                    // append
                    sb.Append(ch);
                    curCol = sb.Length;
                }
                else
                {
                    // overwrite
                    sb[curCol] = ch;
                    curCol++;
                }
                i++;
            }

            // Compose final string
            var outSb = new StringBuilder();
            for (int li = 0; li < lines.Count; li++)
            {
                if (li > 0) outSb.Append('\n');
                outSb.Append(lines[li].ToString());
            }
            return outSb.ToString();
        }

        // Simple terminal emulator wrapper to maintain full state between fragments
        private class TerminalEmulator
        {
            private string _buffer = string.Empty;

            public string Feed(string fragment)
            {
                _buffer = RenderAnsiToVisible(_buffer, fragment);
                return _buffer;
            }

            public string GetVisible() => _buffer;
        }

        private async Task StdoutBaseStreamReaderAsync(Process proc, CancellationToken token)
        {
            try
            {
                var stream = proc.StandardOutput.BaseStream;
                var decoder = Encoding.UTF8.GetDecoder();
                var buffer = new byte[4096];
                var charBuf = new char[4096];
                var existing = string.Empty;
                while (!proc.HasExited && !token.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (read == 0) break;
                    var charsDecoded = decoder.GetChars(buffer, 0, read, charBuf, 0);
                    var fragment = new string(charBuf, 0, charsDecoded);
                    existing = _terminal?.Feed(fragment) ?? RenderAnsiToVisible(existing, fragment);
                    lock (_stdoutBuffer)
                    {
                        _stdoutBuffer.Clear();
                        _stdoutBuffer.Append(existing);
                    }
                    OutputLineReceived?.Invoke(existing);
                }

                // drain remaining
                try
                {
                    var rest = await proc.StandardOutput.ReadToEndAsync();
                    if (!string.IsNullOrEmpty(rest))
                    {
                        existing = RenderAnsiToVisible(existing, rest);
                        lock (_stdoutBuffer)
                        {
                            _stdoutBuffer.Clear();
                            _stdoutBuffer.Append(existing);
                        }
                        OutputLineReceived?.Invoke(existing);
                    }
                }
                catch { }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Error($"Error reading raw stdout: {ex.Message}");
            }
        }
    }
}
