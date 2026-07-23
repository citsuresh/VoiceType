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

        // Background color of the floating waveform pill overlay, as a "#RRGGBB" hex string
        // (no alpha - opacity is controlled separately via PillOpacity).
        public string PillColor { get; set; } = "#283593";

        // Opacity (0.0-1.0) applied to the pill background color.
        public double PillOpacity { get; set; } = 0.9;

        // The dictation (hold-to-talk) hotkey as a single "+"-separated combo, e.g. "Ctrl+LeftAlt"
        // or "F9". The LAST token is the target key; any preceding tokens are modifiers
        // (Ctrl/Shift/Alt/Win). A future voice-command feature will add its own hotkey setting.
        public string DictationHotkey { get; set; } = "Ctrl+Space";

        // When false, the hold-to-talk dictation hotkey is not registered at startup, letting a
        // user who prefers toggle mode disable the hold-to-talk input path entirely.
        public bool DictationHotkeyEnabled { get; set; } = true;

        // Convenience views over DictationHotkey for consumers that need the parts separately
        // (e.g. GlobalHotkeyManager). Not serialized - DictationHotkey is the single source of truth.
        [JsonIgnore]
        public string HotkeyKey => GetHotkeyKey(DictationHotkey);
        [JsonIgnore]
        public string HotkeyModifiers => GetHotkeyModifiers(DictationHotkey);

        // The toggle-mode hotkey as a single "+"-separated combo, e.g. "Ctrl+Shift+Space". A single
        // tap starts/stops a hands-free (toggle) dictation session. Same format as DictationHotkey.
        public string ToggleHotkey { get; set; } = "Ctrl+Shift+Space";

        // When false, toggle mode is disabled entirely: the toggle hotkey is not registered and
        // tray single-click toggle is inactive. Lets a user who prefers hold-to-talk turn it off.
        public bool ToggleModeEnabled { get; set; } = true;

        // Convenience views over ToggleHotkey. Not serialized - ToggleHotkey is the source of truth.
        [JsonIgnore]
        public string ToggleHotkeyKey => GetHotkeyKey(ToggleHotkey);
        [JsonIgnore]
        public string ToggleHotkeyModifiers => GetHotkeyModifiers(ToggleHotkey);

        // Zero-based index of the microphone (WaveIn device) to capture from. 0 = system default.
        public int MicrophoneDeviceIndex { get; set; } = 0;

        // When true, single-clicking the tray icon toggles a hands-free dictation session
        // (start/stop), as an alternative to the hold-to-talk hotkey. Double-click still opens
        // Settings. Enabled by default; kept in sync with the tray context-menu "Toggle mode" item.
        public bool UseTrayIconToggle { get; set; } = true;
        // When true, a tray-toggle session stops automatically after the mic stays idle
        // (below the silence threshold) for ToggleIdleAutoStopSeconds. Ignored for hold-to-talk.
        public bool ToggleIdleAutoStopEnabled { get; set; } = false;
        // Seconds of continuous mic idle before a tray-toggle session auto-stops (when enabled).
        public int ToggleIdleAutoStopSeconds { get; set; } = 5;
        // When true, if no editable control is focused when a transcript is ready, the text is
        // left on the clipboard (so the user can paste it manually) instead of being typed/pasted
        // into a surface that cannot accept it. Applies to both hotkey and tray-toggle modes.
        public bool CopyToClipboardWhenNoEditable { get; set; } = true;
        // When true, show a notification when the transcript is copied to the clipboard as a
        // fallback (because no editable control was focused).
        public bool ShowClipboardCopyNotification { get; set; } = true;
        // Text insertion method: "Clipboard" (Ctrl+V paste) or "Typing" (character-by-character SendInput).
        public string InsertMethod { get; set; } = "Clipboard";
        // When true, the user's previous clipboard content is restored after pasting the
        // transcript. This is disabled by default because async paste targets (WebView2/
        // Electron chat boxes) read the clipboard after our restore runs, causing them to
        // paste the restored (old) content instead of the transcript.
        public bool EnableClipboardRestore { get; set; } = false;

        // Master toggle for persisted transcript history (see TranscriptHistoryService). Enabled
        // by default; users can disable it from Settings if they don't want dictated text kept
        // on disk. The ephemeral post-insertion comparison bulb/preview is unaffected by this setting.
        public bool EnableTranscriptHistory { get; set; } = true;
        // Maximum number of transcript-history entries retained on disk; oldest entries beyond
        // this count are dropped as new ones are added. Matches TranscriptHistoryService's
        // built-in default cap.
        public int TranscriptHistoryRetentionLimit { get; set; } = 50;

        // Post-processing pipeline toggles
        // DictationSessionController.CleanTranscript). Each normalization step is individually
        // toggleable; the built-in non-speech marker/ANSI cleanup always runs regardless.
        public bool PostProcessTrimWhitespace { get; set; } = true;
        public bool PostProcessCollapseSpaces { get; set; } = true;
        public bool PostProcessCapitalizeSentences { get; set; } = true;

        // Master toggle for filler-word removal plus the user-editable list of filler words/phrases.
        // Matching is whole-word and case-insensitive (so "um" isn't stripped from "aluminum").
        // Seeded with non-lexical fillers only; lexical fillers (like/so/actually) are omitted
        // because they are often meaningful.
        public bool RemoveFillerWords { get; set; } = false;
        public System.Collections.Generic.List<string> FillerWords { get; set; } =
            new System.Collections.Generic.List<string>
            {
                "um", "uh", "uhm", "er", "erm", "ah", "mm", "mhm", "hmm", "uh-huh", "uh huh", "mm-hmm"
            };

        // Master toggle for spoken-punctuation replacement plus the user-editable rule list. When
        // enabled, each enabled rule converts a dictated phrase (e.g. "comma", "new paragraph")
        // into its literal replacement. Matching is whole-phrase, case-insensitive, and applies
        // longest phrases first (so "question mark" wins over "mark"). Enabled by default; disable
        // to leave ordinary dictation unchanged.
        public bool EnableSpokenPunctuation { get; set; } = true;
        public System.Collections.Generic.List<SpokenPunctuationRule> SpokenPunctuationRules { get; set; } =
            DefaultSpokenPunctuationRules();

        // Master toggle for custom word replacements plus the user-editable "from -> to" list.
        // Matching is whole-word/whole-phrase and case-insensitive; the replacement text is
        // always inserted exactly as authored (no case-preservation). Empty by default since
        // useful entries are personal/domain-specific (names, jargon, acronyms).
        public bool EnableCustomWordReplacements { get; set; } = false;
        public System.Collections.Generic.List<WordReplacementRule> CustomWordReplacements { get; set; } =
            new System.Collections.Generic.List<WordReplacementRule>();

        // Master toggle for custom phrase-removal rules plus the user-editable rule list. When
        // enabled, each enabled rule strips a dictated phrase from the start, end, or anywhere in
        // a sentence. Matching is whole-word/whole-phrase and case-insensitive. Empty and disabled
        // by default since ordinary dictation should be unaffected unless explicitly configured.
        public bool EnableCustomRemovalRules { get; set; } = false;
        public System.Collections.Generic.List<PhraseRemovalRule> CustomRemovalRules { get; set; } =
            new System.Collections.Generic.List<PhraseRemovalRule>();

        // Sentinel replacement tokens persisted for the two whitespace outputs so the settings
        // file and editor never store raw invisible newline characters. Expanded to real newlines
        // by the post-processing pipeline.
        public const string LineBreakToken = "<Line break>";
        public const string ParagraphBreakToken = "<Paragraph break>";

        /// <summary>
        /// The built-in recommended spoken-punctuation rules. Standard punctuation is enabled by
        /// default; technical-dictation rules are present but disabled so users can opt in without
        /// re-typing them.
        /// </summary>
        public static System.Collections.Generic.List<SpokenPunctuationRule> DefaultSpokenPunctuationRules() =>
            new System.Collections.Generic.List<SpokenPunctuationRule>
            {
                // Standard punctuation (enabled).
                new("comma", ",", true),
                new("period", ".", true),
                new("full stop", ".", true),
                new("question mark", "?", true),
                new("exclamation mark", "!", true),
                new("colon", ":", true),
                new("semicolon", ";", true),
                new("ellipsis", "...", true),
                new("hyphen", "-", true),
                new("dash", "-", true),
                new("quote", "\"", true),
                new("open quote", "\"", true),
                new("close quote", "\"", true),
                new("single quote", "'", true),
                new("open parenthesis", "(", true),
                new("close parenthesis", ")", true),
                new("new line", LineBreakToken, true),
                new("new paragraph", ParagraphBreakToken, true),

                // Technical-dictation rules (present but disabled by default).
                new("slash", "/", false),
                new("backslash", "\\", false),
                new("underscore", "_", false),
                new("at sign", "@", false),
                new("hash", "#", false),
                new("asterisk", "*", false),
                new("equals", "=", false),
                new("plus", "+", false),
                new("open bracket", "[", false),
                new("close bracket", "]", false),
                new("open brace", "{", false),
                new("close brace", "}", false),
            };

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

    /// <summary>
    /// A single spoken-punctuation rule: a dictated <see cref="Phrase"/> that is replaced with
    /// <see cref="Replacement"/> when <see cref="IsEnabled"/> is true. Replacement may be a
    /// literal string or one of the newline tokens on <see cref="VoiceTypeSettings"/>.
    /// </summary>
    public class SpokenPunctuationRule
    {
        public SpokenPunctuationRule() { }

        public SpokenPunctuationRule(string phrase, string replacement, bool isEnabled)
        {
            Phrase = phrase;
            Replacement = replacement;
            IsEnabled = isEnabled;
        }

        public string Phrase { get; set; } = string.Empty;
        public string Replacement { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// A single custom word-replacement rule: a dictated <see cref="From"/> phrase that is
    /// replaced with <see cref="To"/> when <see cref="IsEnabled"/> is true. Matching is
    /// whole-word/whole-phrase and case-insensitive; <see cref="To"/> is inserted exactly as
    /// authored.
    /// </summary>
    public class WordReplacementRule
    {
        public WordReplacementRule() { }

        public WordReplacementRule(string from, string to, bool isEnabled)
        {
            From = from;
            To = to;
            IsEnabled = isEnabled;
        }

        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// Where within a sentence a <see cref="PhraseRemovalRule"/> phrase must appear to be removed.
    /// </summary>
    public enum RemovalScope
    {
        /// <summary>The phrase must appear at the start of a sentence.</summary>
        StartOfSentence = 0,

        /// <summary>The phrase must appear at the end of a sentence.</summary>
        EndOfSentence = 1,

        /// <summary>The phrase may appear anywhere within a sentence.</summary>
        Anywhere = 2
    }

    /// <summary>
    /// A single custom removal rule: a dictated <see cref="Phrase"/> that is stripped out when
    /// <see cref="IsEnabled"/> is true and it appears within the sentence position described by
    /// <see cref="Scope"/>. Matching is whole-word/whole-phrase and case-insensitive.
    /// </summary>
    public class PhraseRemovalRule
    {
        public PhraseRemovalRule() { }

        public PhraseRemovalRule(string phrase, RemovalScope scope, bool isEnabled)
        {
            Phrase = phrase;
            Scope = scope;
            IsEnabled = isEnabled;
        }

        public string Phrase { get; set; } = string.Empty;
        public RemovalScope Scope { get; set; } = RemovalScope.Anywhere;
        public bool IsEnabled { get; set; } = true;
    }
}
