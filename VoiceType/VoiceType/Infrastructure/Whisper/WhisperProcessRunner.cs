using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using VoiceType.Infrastructure.Config;
using VoiceType.Models;

namespace VoiceType.Infrastructure.Whisper
{
    public class WhisperProcessRunner
    {
        private readonly VoiceTypeSettings _settings;

        public WhisperProcessRunner(VoiceTypeSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<FinalTranscriptionResult> RunAsync(string wavPath)
        {
            // Choose which executable to prefer based on transcription mode
            var configuredExecutable = _settings.Mode == TranscriptionMode.Cli
                ? (_settings.WhisperCliExecutablePath ?? _settings.WhisperStreamExecutablePath)
                : (_settings.WhisperStreamExecutablePath ?? _settings.WhisperCliExecutablePath);
            var exePath = ResolveExecutablePath(configuredExecutable);
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return new FinalTranscriptionResult { Success = false, ErrorMessage = $"Whisper executable not found (tried '{configuredExecutable}')" };
            }

            if (!File.Exists(wavPath))
            {
                return new FinalTranscriptionResult { Success = false, ErrorMessage = "Input WAV not found" };
            }

            var modelPath = ResolveModelPath(_settings.WhisperModelPath);
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                // fall back to configured path and allow whisper to error if missing
                modelPath = _settings.WhisperModelPath ?? string.Empty;
            }

            // Build arguments depending on whether we are in Cli mode for advanced options or standard WavFile/Stream mode
            string arguments;
            if (_settings.Mode == TranscriptionMode.Cli)
            {
                var sb = new StringBuilder();
                sb.Append($"-m \"{modelPath}\" -f \"{wavPath}\"");
                if (!string.IsNullOrWhiteSpace(_settings.Language)) sb.Append($" -l {_settings.Language}");
                if (_settings.WhisperCliTranslate) sb.Append(" -tr");
                if (_settings.WhisperCliThreads > 0) sb.Append($" -t {_settings.WhisperCliThreads}");
                if (_settings.WhisperCliNoTimestamps) sb.Append(" -nt");
                if (_settings.WhisperCliNoPrints) sb.Append(" -np");
                if (!string.IsNullOrWhiteSpace(_settings.WhisperCliOutputFile)) sb.Append($" -of \"{_settings.WhisperCliOutputFile}\"");
                if (!string.IsNullOrWhiteSpace(_settings.WhisperCliArguments)) sb.Append(' ').Append(_settings.WhisperCliArguments.Trim());
                arguments = sb.ToString();
            }
            else
            {
                // Default CLI-style invocation for non-CLI fallback (keeps previous behavior)
                arguments = $"-m \"{modelPath}\" -f \"{wavPath}\" -l {_settings.Language} --no-timestamps";
            }

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) return new FinalTranscriptionResult { Success = false, ErrorMessage = "Failed to start whisper process" };

                var stdout = await proc.StandardOutput.ReadToEndAsync();
                var stderr = await proc.StandardError.ReadToEndAsync();
                proc.WaitForExit();
                var exit = proc.ExitCode;

                if (exit != 0)
                {
                    return new FinalTranscriptionResult { Success = false, ErrorMessage = stderr, ExitCode = exit };
                }

                // heuristically take stdout as the transcript
                var text = stdout?.Trim() ?? string.Empty;
                //MessageBox.Show(text);
                return new FinalTranscriptionResult { Success = true, Text = text, ExitCode = exit };
            }
            catch (Exception ex)
            {
                return new FinalTranscriptionResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private string? ResolveExecutablePath(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);

            // Try relative to app base directory
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var trial = Path.Combine(baseDir, configured);
                if (File.Exists(trial)) return trial;
            }

            // Try to locate a whisper.cpp folder up the directory tree
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++)
            {

                // Common locations and executable names
                var candidates = new[]
                {
                    Path.Combine(dir.FullName, "whisper.cpp", "whisper", "Release", "whisper-stream.exe"),
                    Path.Combine(dir.FullName, "whisper.cpp", "whisper", "Release", "whisper-cli.exe"),
                    Path.Combine(dir.FullName, "whisper.cpp", "whisper", "Release", "whisper.exe"),
                    Path.Combine(dir.FullName, "whisper.cpp", "Release", "whisper-stream.exe"),
                    Path.Combine(dir.FullName, "whisper.cpp", "Release", "whisper-cli.exe"),
                    Path.Combine(dir.FullName, "whisper.cpp", "Release", "whisper.exe"),
                    Path.Combine(dir.FullName, "whisper", "Release", "whisper-stream.exe"),
                    Path.Combine(dir.FullName, "whisper", "Release", "whisper-cli.exe"),
                    Path.Combine(dir.FullName, "whisper", "Release", "whisper.exe"),
                    Path.Combine(dir.FullName, "whisper.cpp", "whisper.exe"),
                    Path.Combine(dir.FullName, "whisper", "whisper-stream.exe"),
                };

                foreach (var candidate in candidates)
                {
                    if (File.Exists(candidate)) return candidate;
                }

                dir = dir.Parent;
            }

            return null;
        }

        private string? ResolveModelPath(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);

            // Try relative to app base directory
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var trial = Path.Combine(baseDir, configured);
                if (File.Exists(trial)) return trial;
            }

            // Search for ggml model under nearby whisper.cpp models folders
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var modelsDir = Path.Combine(dir.FullName, "whisper.cpp", "models");
                if (Directory.Exists(modelsDir))
                {
                    var files = Directory.GetFiles(modelsDir, "ggml-*.bin");
                    if (files.Length > 0) return files[0];
                }
                dir = dir.Parent;
            }

            return null;
        }

        public (string? ExecutablePath, string? ModelPath) ProbePaths()
        {
            var exe = ResolveExecutablePath(_settings.WhisperStreamExecutablePath) ?? ResolveExecutablePath(_settings.WhisperCliExecutablePath);
            var model = ResolveModelPath(_settings.WhisperModelPath);
            return (exe, model);
        }

        /// <summary>
        /// Resolves the whisper.cpp models directory by walking up from the app base directory,
        /// preferring the folder that actually contains the currently configured model. Returns
        /// null when no models folder can be located.
        /// </summary>
        public string? ResolveModelsDirectory()
        {
            // Prefer the directory of the currently configured model when it resolves to a file.
            var configuredModel = ResolveModelPath(_settings.WhisperModelPath);
            if (!string.IsNullOrEmpty(configuredModel))
            {
                var dir = Path.GetDirectoryName(configuredModel);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }

            var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && baseDir != null; i++)
            {
                var modelsDir = Path.Combine(baseDir.FullName, "whisper.cpp", "models");
                if (Directory.Exists(modelsDir)) return modelsDir;
                baseDir = baseDir.Parent;
            }

            return null;
        }

        /// <summary>
        /// Enumerates the available whisper models (ggml-*.bin) in the resolved models directory,
        /// ordered by file name. Returns an empty array when no models folder or files are found.
        /// </summary>
        public string[] EnumerateModels()
        {
            var modelsDir = ResolveModelsDirectory();
            if (string.IsNullOrEmpty(modelsDir) || !Directory.Exists(modelsDir))
                return Array.Empty<string>();

            var files = Directory.GetFiles(modelsDir, "ggml-*.bin");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }
    }
}
