using System.Text.Json.Serialization;

namespace VoiceType.Infrastructure.Config
{
    /// <summary>
    /// Specifies which transcription backend to use for converting audio to text.
    /// </summary>
    public enum TranscriptionMode
    {
        /// <summary>
        /// Record audio to a WAV file, then transcribe with whisper.exe after recording completes.
        /// </summary>
        WavFile = 0,

        /// <summary>
        /// Stream audio in real-time to whisper-stream for live transcription.
        /// </summary>
        Stream = 1,

        /// <summary>
        /// Record audio to a WAV file, then transcribe with whisper-cli.exe for advanced options.
        /// </summary>
        Cli = 2,

        /// <summary>
        /// Record audio to a WAV file, then transcribe it via a long-lived whisper-server.exe
        /// process (model kept loaded) over HTTP. Same single-pass accuracy as CLI, but without
        /// the per-utterance process-spawn and model-reload overhead.
        /// </summary>
        Server = 3
    }

    public class VoiceTypeSettings
    {
        public string WhisperStreamExecutablePath { get; set; } = "./whisper.cpp/whisper.exe";
        // Backwards-compatible alias for older configurations referencing WhisperExecutablePath.
        // Ignored during (de)serialization so persistence writes a single canonical key
        // (WhisperStreamExecutablePath) instead of a duplicate.
        [JsonIgnore]
        public string WhisperExecutablePath
        {
            get => WhisperStreamExecutablePath;
            set => WhisperStreamExecutablePath = value;
        }
        public string WhisperCliExecutablePath { get; set; } = "./whisper.cpp/whisper-cli.exe";
        // Path to whisper-server.exe, used when Mode == Server.
        public string WhisperServerExecutablePath { get; set; } = "./whisper.cpp/whisper-cli.exe";
        // Host/port the long-lived whisper-server listens on (Server mode only).
        public string WhisperServerHost { get; set; } = "127.0.0.1";
        public int WhisperServerPort { get; set; } = 8080;
        // Extra whisper-server launch flags (Server mode only), e.g. decoding options
        // like "-bs 8 -bo 8 -mc 0" to improve accuracy. Appended verbatim to the command line.
        public string WhisperServerArguments { get; set; } = "";
        // Maximum time (seconds) to wait for a single whisper-server /inference request before
        // aborting it (Server mode only). Larger models on low-memory machines can be slow; if
        // requests time out, either raise this or switch to a smaller model.
        public int WhisperServerTimeoutSeconds { get; set; } = 30;
        // Transcription mode: WavFile (record then transcribe with whisper.exe), 
        // Stream (real-time whisper-stream), or Cli (record then transcribe with whisper-cli.exe).
        public TranscriptionMode Mode { get; set; } = TranscriptionMode.WavFile;
        // whisper-cli specific options
        public bool WhisperCliTranslate { get; set; } = false;
        public int WhisperCliThreads { get; set; } = 0; // 0 = let CLI decide / autodetect
        public bool WhisperCliNoTimestamps { get; set; } = true;
        public bool WhisperCliNoPrints { get; set; } = false;
        public string WhisperCliOutputFile { get; set; } = ""; // base output file path (no extension)
        public string WhisperCliArguments { get; set; } = "";
        public string WhisperModelPath { get; set; } = "./models/ggml-base.bin";
        public string WhisperStreamArguments { get; set; } = "";
        public string TempDirectory { get; set; } = "./temp";
        public string Language { get; set; } = "en";
        public int PreviewChunkMilliseconds { get; set; } = 2000;
        public int PreviewThrottleMilliseconds { get; set; } = 1000;

        // The dictation (hold-to-talk) hotkey as a single "+"-separated combo, e.g. "Ctrl+LeftAlt"
        // or "F9". The LAST token is the target key; any preceding tokens are modifiers
        // (Ctrl/Shift/Alt/Win). A future voice-command feature will add its own hotkey setting.
        public string DictationHotkey { get; set; } = "Ctrl+LeftAlt";

        // Convenience views over DictationHotkey for consumers that need the parts separately
        // (e.g. GlobalHotkeyManager). Not serialized - DictationHotkey is the single source of truth.
        [JsonIgnore]
        public string HotkeyKey => GetHotkeyKey(DictationHotkey);
        [JsonIgnore]
        public string HotkeyModifiers => GetHotkeyModifiers(DictationHotkey);

        // Zero-based index of the microphone (WaveIn device) to capture from. 0 = system default.
        public int MicrophoneDeviceIndex { get; set; } = 0;
        // Text insertion method: "Clipboard" (Ctrl+V paste) or "Typing" (character-by-character SendInput).
        public string InsertMethod { get; set; } = "Clipboard";
        // When true, the user's previous clipboard content is restored after pasting the
        // transcript. This is disabled by default because async paste targets (WebView2/
        // Electron chat boxes) read the clipboard after our restore runs, causing them to
        // paste the restored (old) content instead of the transcript.
        public bool EnableClipboardRestore { get; set; } = false;

        // Captures any JSON properties not represented by the strongly-typed members above so
        // they round-trip untouched through Load/Save. This keeps future (or non-settings)
        // configuration sections in appsettings.json from being dropped when the user saves.
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }

        // Split characters accepted between hotkey tokens.
        private static readonly char[] HotkeySeparators = { '+', ',', '|' };

        /// <summary>
        /// Extracts the target key (the last token) from a combined hotkey string such as
        /// "Ctrl+LeftAlt" or "F9". Returns an empty string when none is present.
        /// </summary>
        public static string GetHotkeyKey(string? combined)
        {
            if (string.IsNullOrWhiteSpace(combined)) return string.Empty;
            var tokens = combined.Split(HotkeySeparators, System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            return tokens.Length == 0 ? string.Empty : tokens[^1];
        }

        /// <summary>
        /// Extracts the modifiers (all tokens before the last) from a combined hotkey string as a
        /// "+"-separated list, e.g. "Ctrl+Shift". Returns an empty string when there are none.
        /// </summary>
        public static string GetHotkeyModifiers(string? combined)
        {
            if (string.IsNullOrWhiteSpace(combined)) return string.Empty;
            var tokens = combined.Split(HotkeySeparators, System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            return tokens.Length <= 1 ? string.Empty : string.Join("+", tokens[..^1]);
        }

        /// <summary>
        /// Builds a combined hotkey string from separate modifier and key parts, e.g.
        /// ("Ctrl", "LeftAlt") => "Ctrl+LeftAlt". A blank modifier yields just the key.
        /// </summary>
        public static string CombineHotkey(string? modifiers, string? key)
        {
            key = (key ?? string.Empty).Trim();
            modifiers = (modifiers ?? string.Empty).Trim();
            return string.IsNullOrEmpty(modifiers) ? key : $"{modifiers}+{key}";
        }
    }
}
