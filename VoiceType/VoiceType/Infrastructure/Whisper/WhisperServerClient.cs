using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VoiceType.Infrastructure.Config;
using VoiceType.Infrastructure.Logging;
using VoiceType.Models;

namespace VoiceType.Infrastructure.Whisper
{
    /// <summary>
    /// Owns a long-lived whisper-server.exe process (model kept loaded) and transcribes
    /// WAV files via its HTTP <c>/inference</c> endpoint. Unlike Stream mode this performs a
    /// single clean pass per utterance (no sliding-window repetition), while avoiding the
    /// per-utterance process-spawn and model-reload cost of CLI mode.
    /// </summary>
    public sealed class WhisperServerClient : IDisposable
    {
        private readonly VoiceTypeSettings _settings;
        private readonly HttpClient _http;
        private readonly ChildProcessJob? _job;
        private Process? _proc;

        // Bounded ring buffer of the server's own stdout/stderr lines. Only ever written to
        // voicetype.log when a request comes back empty or fails (see TranscribeAsync) - not on
        // every successful call - so normal dictation doesn't bloat the log file. Capped so the
        // buffer itself can't grow unbounded even during a long-running server.
        private readonly List<string> _serverOutputLog = new List<string>();
        private const int MaxServerOutputLines = 300;
        private volatile bool _ready;

        // Serializes process lifecycle operations (start/stop/restart) so a runtime model switch
        // can't race an in-flight start or another switch.
        private readonly SemaphoreSlim _lifecycleLock = new SemaphoreSlim(1, 1);

        public WhisperServerClient(VoiceTypeSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // Per-request timeouts are enforced with a linked CancellationTokenSource in
            // TranscribeAsync so we can tell a timeout apart from other failures. Disable the
            // HttpClient-level timeout (InfiniteTimeSpan) to avoid a second, ambiguous timeout.
            _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            // Best-effort: if the job object can't be created we still run, relying on Dispose()
            // for graceful shutdown. The job additionally guarantees the child dies on a hard kill.
            try { _job = new ChildProcessJob(); }
            catch (Exception ex) { Logger.Error($"WhisperServerClient: could not create child-process job: {ex.Message}"); }
        }

        // Per-request inference timeout from settings (seconds), clamped to a sane minimum.
        private TimeSpan InferenceTimeout =>
            TimeSpan.FromSeconds(Math.Max(1, _settings.WhisperServerTimeoutSeconds));

        private string BaseUrl =>
            $"http://{_settings.WhisperServerHost}:{_settings.WhisperServerPort}";

        /// <summary>
        /// Whether the server process is running and has reported that it is listening.
        /// </summary>
        public bool IsReady => _ready && _proc is { HasExited: false };

        /// <summary>
        /// Starts whisper-server.exe and waits until it reports it is listening (or the
        /// timeout elapses). Returns true when the server is ready to accept requests.
        /// </summary>
        public async Task<bool> StartAsync(int readyTimeoutMs = 20000, CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await StartCoreAsync(readyTimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        // Starts the server without acquiring the lifecycle lock. Callers that already hold
        // _lifecycleLock (StartAsync, RestartAsync) use this to avoid re-entrant deadlock.
        private async Task<bool> StartCoreAsync(int readyTimeoutMs, CancellationToken cancellationToken)
        {
            if (IsReady) return true;

            var exe = ResolveServerExecutablePath();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Logger.Error($"WhisperServerClient: whisper-server.exe not found (configured '{_settings.WhisperServerExecutablePath}').");
                return false;
            }

            var probe = new WhisperProcessRunner(_settings).ProbePaths();
            var model = probe.ModelPath ?? _settings.WhisperModelPath ?? string.Empty;
            if (string.IsNullOrEmpty(model) || !File.Exists(model))
            {
                Logger.Error($"WhisperServerClient: model not found (configured '{_settings.WhisperModelPath}').");
                return false;
            }

            var args = new StringBuilder();
            args.Append($"-m \"{model}\"");
            if (!string.IsNullOrWhiteSpace(_settings.Language)) args.Append($" -l {_settings.Language}");
            args.Append($" --host {_settings.WhisperServerHost} --port {_settings.WhisperServerPort}");
            // Optional decoding/accuracy flags (e.g. "-bs 8 -bo 8 -mc 0").
            if (!string.IsNullOrWhiteSpace(_settings.WhisperServerArguments))
                args.Append(' ').Append(_settings.WhisperServerArguments.Trim());

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args.ToString(),
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            // Signalled when the server process exits before becoming ready, so we can stop
            // polling immediately instead of waiting out the full timeout.
            var exitedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                Logger.Info($"WhisperServerClient: starting {exe} {psi.Arguments}");
                _proc = Process.Start(psi);
                if (_proc == null)
                {
                    Logger.Error("WhisperServerClient: failed to start whisper-server process.");
                    return false;
                }

                // Tie the child's lifetime to ours: if this app dies for any reason (including a
                // hard kill or crash that skips Dispose), the OS terminates whisper-server so it
                // can't orphan and hold the listening port.
                if (_job != null && !_job.AssignProcess(_proc.Handle))
                    Logger.Error("WhisperServerClient: failed to assign server to child-process job.");

                // Drain stdout/stderr so the child never blocks on a full pipe; capture both into
                // a bounded ring buffer for diagnostics. Readiness itself is detected by HTTP
                // polling below, which is far more reliable than scraping the console banner.
                _proc.OutputDataReceived += (_, e) => AppendServerOutputLine(e.Data);
                _proc.ErrorDataReceived += (_, e) => AppendServerOutputLine(e.Data);
                _proc.EnableRaisingEvents = true;
                _proc.Exited += (_, __) => exitedTcs.TrySetResult(true);
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();

                if (await WaitForServerReadyAsync(readyTimeoutMs, exitedTcs.Task, cancellationToken).ConfigureAwait(false))
                {
                    _ready = true;
                    Logger.Info($"WhisperServerClient: server ready at {BaseUrl}.");
                    return true;
                }

                Logger.Error($"WhisperServerClient: server did not become ready within {readyTimeoutMs} ms.");
                LogServerOutputSince(0, "server did not become ready within timeout");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error($"WhisperServerClient: error starting server: {ex}");
                return false;
            }
        }

        // Polls the server with lightweight HTTP requests until it responds (any HTTP status
        // means the listener is up), the process exits, or the timeout elapses.
        private async Task<bool> WaitForServerReadyAsync(int timeoutMs, Task processExited, CancellationToken cancellationToken)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            using var probeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

            while (Environment.TickCount64 < deadline)
            {
                if (processExited.IsCompleted)
                {
                    Logger.Error("WhisperServerClient: server process exited before becoming ready.");
                    LogServerOutputSince(0, "server exited before becoming ready");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Any response (even 404/405) proves the HTTP listener is accepting requests.
                    using var resp = await probeClient.GetAsync(BaseUrl, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (HttpRequestException)
                {
                    // Not listening yet; keep polling.
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Per-request timeout; treat as not-ready-yet and keep polling.
                }

                try { await Task.Delay(200, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
            }

            return false;
        }

        /// <summary>
        /// Stops the running whisper-server process (if any) and waits for it to exit. Safe to
        /// call when no server is running.
        /// </summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                StopCore();
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// Restarts the server with a new model. Cleanly stops the current process, applies
        /// <paramref name="modelPath"/> to the shared settings, and starts a fresh server with
        /// the new <c>-m</c> argument, reusing the standard readiness polling. Returns true when
        /// the new server becomes ready.
        /// </summary>
        public async Task<bool> RestartAsync(string modelPath, int readyTimeoutMs = 20000, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("Model path must be provided.", nameof(modelPath));

            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                StopCore();
                _settings.WhisperModelPath = modelPath;
                Logger.Info($"WhisperServerClient: restarting with model '{modelPath}'.");
                return await StartCoreAsync(readyTimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        // Kills the current server process (if running) and clears readiness. Callers must hold
        // _lifecycleLock. Leaves the child-process job and HttpClient intact for reuse.
        private void StopCore()
        {
            _ready = false;
            try
            {
                if (_proc is { HasExited: false })
                {
                    _proc.Kill(entireProcessTree: true);
                    _proc.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"WhisperServerClient: error stopping server: {ex.Message}");
            }
            finally
            {
                try { _proc?.Dispose(); } catch { }
                _proc = null;
                lock (_serverOutputLog) { _serverOutputLog.Clear(); }
            }
        }

        /// <summary>
        /// Returns a failed result (with an error message) if the server is not ready or the
        /// request fails, so callers can fall back to CLI.
        /// </summary>
        public async Task<FinalTranscriptionResult> TranscribeAsync(string wavPath, CancellationToken cancellationToken = default)
        {
            if (!IsReady)
                return new FinalTranscriptionResult { Success = false, ErrorMessage = "Whisper server is not ready." };

            if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
                return new FinalTranscriptionResult { Success = false, ErrorMessage = "Input WAV not found." };

            // Mark where this request's server console output starts so we can log just the
            // relevant slice (see LogServerOutputSince) if the result turns out empty or failed.
            int outputStartIndex;
            lock (_serverOutputLog) { outputStartIndex = _serverOutputLog.Count; }

            try
            {
                using var form = new MultipartFormDataContent();
                await using var fileStream = File.OpenRead(wavPath);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                form.Add(fileContent, "file", Path.GetFileName(wavPath));
                form.Add(new StringContent("json"), "response_format");
                form.Add(new StringContent("true"), "no_timestamps");
                if (!string.IsNullOrWhiteSpace(_settings.Language))
                    form.Add(new StringContent(_settings.Language), "language");

                // Enforce the per-request timeout with a linked CTS so a timeout is distinguishable
                // from other cancellation/failure causes.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(InferenceTimeout);

                var url = $"{BaseUrl}{"/inference"}";
                using var resp = await _http.PostAsync(url, form, timeoutCts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    LogServerOutputSince(outputStartIndex, $"server returned {(int)resp.StatusCode} {resp.ReasonPhrase}");
                    return new FinalTranscriptionResult
                    {
                        Success = false,
                        ErrorMessage = $"Server returned {(int)resp.StatusCode} {resp.ReasonPhrase}",
                        ExitCode = (int)resp.StatusCode
                    };
                }

                await using var body = await resp.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(body, cancellationToken: timeoutCts.Token).ConfigureAwait(false);
                var text = doc.RootElement.TryGetProperty("text", out var textEl)
                    ? textEl.GetString() ?? string.Empty
                    : string.Empty;

                var trimmed = text.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    LogServerOutputSince(outputStartIndex, "server returned an empty transcription");

                return new FinalTranscriptionResult { Success = true, Text = trimmed };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The linked token fired but the caller did not cancel: this is our inference timeout.
                var seconds = (int)InferenceTimeout.TotalSeconds;
                Logger.Error($"WhisperServerClient: inference timed out after {seconds}s.");
                LogServerOutputSince(outputStartIndex, "inference timed out");
                return new FinalTranscriptionResult
                {
                    Success = false,
                    TimedOut = true,
                    ErrorMessage = $"Transcription timed out after {seconds} seconds."
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"WhisperServerClient: inference request failed: {ex.Message}");
                LogServerOutputSince(outputStartIndex, $"inference request failed: {ex.Message}");
                return new FinalTranscriptionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        // Appends one line of the server's stdout/stderr to the bounded ring buffer, trimming the
        // oldest lines once the cap is exceeded so long-running sessions can't grow it unbounded.
        private void AppendServerOutputLine(string? line)
        {
            if (line == null) return;
            lock (_serverOutputLog)
            {
                _serverOutputLog.Add(line);
                var excess = _serverOutputLog.Count - MaxServerOutputLines;
                if (excess > 0) _serverOutputLog.RemoveRange(0, excess);
            }
        }

        // Writes the server's console output captured since <paramref name="startIndex"/> to
        // voicetype.log. Only called for empty/failed transcriptions (see TranscribeAsync) so
        // normal, successful dictations never grow the log with per-request server chatter.
        private void LogServerOutputSince(int startIndex, string reason)
        {
            string[] lines;
            lock (_serverOutputLog)
            {
                // startIndex may be stale if the buffer was trimmed/cleared since the request
                // started (e.g. a restart raced this call); clamp defensively.
                var clamped = Math.Min(startIndex, _serverOutputLog.Count);
                lines = _serverOutputLog.Skip(clamped).ToArray();
            }

            if (lines.Length == 0)
            {
                Logger.Info($"WhisperServerClient: {reason}; no whisper-server console output was captured for this request.");
                return;
            }

            Logger.Info($"WhisperServerClient: {reason}; whisper-server output for this request:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
        }

        // Resolves the whisper-server.exe path: prefer the configured path, otherwise place it
        // next to the whisper executable resolved by the standard probe (they are co-located
        // in the Release folder).
        private string? ResolveServerExecutablePath()
        {
            var configured = _settings.WhisperServerExecutablePath;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (File.Exists(configured)) return Path.GetFullPath(configured);
                var trial = Path.Combine(AppContext.BaseDirectory, configured);
                if (File.Exists(trial)) return trial;
            }

            var probe = new WhisperProcessRunner(_settings).ProbePaths();
            var refExe = probe.ExecutablePath;
            if (!string.IsNullOrEmpty(refExe))
            {
                var dir = Path.GetDirectoryName(refExe);
                if (!string.IsNullOrEmpty(dir))
                {
                    var candidate = Path.Combine(dir, "whisper-server.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }

            return null;
        }

        public void Dispose()
        {
            try
            {
                StopCore();
            }
            catch (Exception ex)
            {
                Logger.Error($"WhisperServerClient: error stopping server: {ex.Message}");
            }
            finally
            {
                try { _job?.Dispose(); } catch { }
                try { _http.Dispose(); } catch { }
                try { _lifecycleLock.Dispose(); } catch { }
            }
        }
    }
}
