using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VoiceType.Infrastructure.Config;
using VoiceType.Infrastructure.Logging;
using System.Text.RegularExpressions;
using VoiceType.Core.Diff;
using VoiceType.Core.Preview;
using VoiceType.Infrastructure.History;
using VoiceType.Models;

namespace VoiceType.Core
{
    public class DictationSessionController : IDisposable
    {
        private Infrastructure.Whisper.WhisperStreamClient? _streamClient;
        private Action<string>? _streamOutputHandler;
        private Action<string>? _streamErrorHandler;
        private readonly VoiceTypeSettings _settings;

        // Optional long-lived whisper-server client, injected by App when Mode == Server.
        // When set and ready, the non-stream transcription path uses it (with CLI fallback).
        public Infrastructure.Whisper.WhisperServerClient? ServerClient { get; set; }

        // Optional transcript-comparison history persistence, injected by App at startup. When
        // set, a successful insertion whose post-processing changed the transcript is recorded here
        // and surfaced via a post-insertion bulb (see RecordComparisonAndShowBulb).
        public TranscriptHistoryService? HistoryService { get; set; }

        // Optional shared "latest comparison" state, injected by App at startup, so the bulb/
        // comparison popup can retrieve the current entry without a direct call-site dependency.
        public TranscriptPreviewState? PreviewState { get; set; }
        private DictationState _state = DictationState.Idle;
        private bool _pendingStartRequest = false;
        // Monotonic id bumped each time a session transitions Idle -> Listening. A start runs
        // several awaits (foreground capture, overlay creation) before the mic actually starts;
        // a very fast hotkey release can flip the state to Finalizing during those awaits. The
        // resumed start compares this id to detect it was superseded and abort instead of
        // starting the microphone (which would leave the app "stuck" listening).
        private int _sessionGeneration = 0;
        private readonly object _sync = new object();

        /// <summary>
        /// Raised whenever the dictation state changes (e.g. Idle -> Listening -> Finalizing ->
        /// Idle). Fired outside the internal lock. Handlers must be thread-safe / marshal to the
        /// UI thread themselves. Used by the tray icon to reflect recording state.
        /// </summary>
        public event Action<DictationState>? StateChanged;

        private void RaiseStateChanged(DictationState state)
        {
            try
            {
                StateChanged?.Invoke(state);
            }
            catch (Exception ex)
            {
                Logger.Error($"StateChanged handler failed: {ex}");
            }
        }

        // Idle auto-stop (used only by tray-toggle sessions). When armed, a lightweight timer
        // stops the session after the mic stays below the silence threshold for the configured
        // number of seconds. A short warm-up grace avoids stopping before the user starts talking.
        private System.Threading.Timer? _idleMonitorTimer;
        private volatile bool _idleAutoStopArmed;
        private long _lastVoiceActivityTicks;
        private int _idleAutoStopSeconds = 5;
        private DateTime _idleArmTimeUtc;
        private const double IdleSilenceThreshold = 0.02; // normalized level (0..1) below = idle
        private const double IdleWarmupSeconds = 1.5;

        /// <summary>
        /// Arms idle auto-stop for the current/next session. Safe to call from any thread.
        /// </summary>
        public void ArmIdleAutoStop(int idleSeconds)
        {
            _idleAutoStopSeconds = Math.Max(1, idleSeconds);
            _idleArmTimeUtc = DateTime.UtcNow;
            Interlocked.Exchange(ref _lastVoiceActivityTicks, DateTime.UtcNow.Ticks);
            _idleAutoStopArmed = true;
            _idleMonitorTimer ??= new System.Threading.Timer(IdleMonitorTick, null, 500, 500);
            Logger.Info($"Idle auto-stop armed: {_idleAutoStopSeconds}s");
        }

        /// <summary>
        /// Disarms idle auto-stop and disposes the monitor timer. Safe to call from any thread.
        /// </summary>
        public void DisarmIdleAutoStop()
        {
            if (!_idleAutoStopArmed && _idleMonitorTimer is null) return;
            _idleAutoStopArmed = false;
            try { _idleMonitorTimer?.Dispose(); } catch { }
            _idleMonitorTimer = null;
        }

        // Records mic activity from the audio-level signal; resets the idle timer when the level
        // exceeds the silence threshold.
        private void NoteAudioActivity(double level)
        {
            if (!_idleAutoStopArmed) return;
            if (level >= IdleSilenceThreshold)
                Interlocked.Exchange(ref _lastVoiceActivityTicks, DateTime.UtcNow.Ticks);
        }

        private void IdleMonitorTick(object? state)
        {
            if (!_idleAutoStopArmed) return;
            if (_state != DictationState.Listening && _state != DictationState.Previewing) return;
            if ((DateTime.UtcNow - _idleArmTimeUtc).TotalSeconds < IdleWarmupSeconds) return;

            var lastActivity = new DateTime(Interlocked.Read(ref _lastVoiceActivityTicks), DateTimeKind.Utc);
            if ((DateTime.UtcNow - lastActivity).TotalSeconds < _idleAutoStopSeconds) return;

            Logger.Info("Idle auto-stop: mic idle threshold reached, stopping session.");
            _idleAutoStopArmed = false;
            _ = Task.Run(async () =>
            {
                try { await StopSessionAsync().ConfigureAwait(false); }
                catch (Exception ex) { Logger.Error($"Idle auto-stop StopSessionAsync failed: {ex}"); }
            });
        }

        /// <summary>
        /// Computes the display model name (file name without extension) from the current settings.
        /// </summary>
        private string CurrentModelDisplayName =>
            string.IsNullOrWhiteSpace(_settings.WhisperModelPath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(_settings.WhisperModelPath);

        /// <summary>
        /// Refreshes the model name shown on the overlay bubble from the current settings. The
        /// next session always reads settings fresh; this additionally updates a currently
        /// visible pill so a runtime model switch is reflected immediately. Safe to call from
        /// any thread.
        /// </summary>
        public void RefreshModelName()
        {
            var name = CurrentModelDisplayName;
            var dispatcher = Application.Current?.Dispatcher;

            void Apply()
            {
                try
                {
                    if (_overlayViewModel != null) _overlayViewModel.ModelName = name;
                    _breathingWindow?.SetModelName(name);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error refreshing overlay model name: {ex}");
                }
            }

            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }

        /// <summary>
        /// Shows a standalone status pill (bottom-center, click-through) displaying an animated
        /// "<paramref name="text"/>" message until <see cref="CloseStatusPill"/> is called. Used
        /// for transient background operations outside a dictation session, e.g. model switching.
        /// Safe to call from any thread.
        /// </summary>
        public void ShowStatusPill(string text)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void Apply()
            {
                try
                {
                    if (_statusWindow == null)
                    {
                        _statusWindow = new UI.BreathingOverlayWindow
                        {
                            ShowActivated = false,
                            Topmost = true
                        };
                        _statusWindow.Closed += (_, _) => _statusWindow = null;
                        _statusWindow.Show();
                        _statusWindow.ApplyAppearance(_settings);
                        _statusWindow.SetModelName(CurrentModelDisplayName);
                        _statusWindow.PositionBottomCenter();
                    }

                    _statusWindow.ShowProcessing(text);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error showing status pill: {ex}");
                }
            }

            if (!dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }

        /// <summary>
        /// Closes the standalone status pill shown by <see cref="ShowStatusPill"/>, if any.
        /// Safe to call from any thread.
        /// </summary>
        public void CloseStatusPill()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void Apply()
            {
                try
                {
                    _statusWindow?.Close();
                    _statusWindow = null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error closing status pill: {ex}");
                }
            }

            if (!dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }

        // overlay and viewmodel instances
        private UI.CompactOverlayWindow? _overlayWindow;
        private UI.BreathingOverlayWindow? _breathingWindow;
        // Standalone pill used for transient status outside a dictation session (e.g. model
        // switching), so it never interferes with the session-owned _breathingWindow.
        private UI.BreathingOverlayWindow? _statusWindow;
        private UI.ViewModels.FloatingOverlayViewModel? _overlayViewModel;
        private Infrastructure.Audio.AudioCaptureService? _audioCapture;
        private string? _currentSessionFilePath;
        private DateTime _lastRawAudioLog = DateTime.MinValue;
        // waveform throttling helpers
        private readonly object _waveformLock = new object();
        private double _pendingWaveformAmplitude = 0.0;
        private DateTime _lastWaveformUpdate = DateTime.MinValue;
        private int _waveformThrottleMs = 8; // ~125Hz for smooth scrolling
        // display smoothing: fast attack, moderate decay
        private double _lastWaveformDisplay = 0.0;
        private double _waveformDecay = 0.55;

        // Tracks whether the current session's microphone has delivered its first audio buffer.
        // Used to switch the pill from the "Starting mic" preparing state to the listening bars.
        private int _micLive = 0;

        // Highest normalised audio level (0..1) seen during the current session. Used to tell a
        // genuine "no speech" (silence) apart from an empty transcription that had audio present
        // (e.g. a server timeout), so the user gets an accurate message. Guarded by _waveformLock.
        private double _sessionPeakAudioLevel = 0.0;

        // Minimum session peak level that counts as the user having actually spoken. Below this the
        // capture is treated as effectively silent. Silence logs ~0.000 while normal speech peaks
        // well above this, so a small threshold reliably separates the two cases.
        private const double SpeechDetectedPeakThreshold = 0.02;

        private void EnsureAudioCaptureInitialized()
        {
            // If the configured microphone changed since the cached instance was created, dispose
            // it so the next session captures from the newly selected device. WaveInEvent binds
            // its device index at construction, so a new instance is required to switch mics.
            if (_audioCapture != null && _audioCapture.DeviceNumber != _settings.MicrophoneDeviceIndex)
            {
                try { _audioCapture.Dispose(); }
                catch (Exception ex) { Logger.Error($"Error disposing audio capture on mic change: {ex}"); }
                _audioCapture = null;
            }

            if (_audioCapture != null) return;

            _audioCapture = new Infrastructure.Audio.AudioCaptureService(sampleRate: 16000, channels: 1, deviceNumber: _settings.MicrophoneDeviceIndex);

            // forward audio level to overlay safely
            _audioCapture.AudioLevelUpdated += (s, level) =>
            {
                try
                {
                    NoteAudioActivity(level);

                    var vm = _overlayViewModel;
                    if (vm != null)
                    {
                        var disp = Application.Current?.Dispatcher;
                        if (disp != null)
                        {
                            disp.Invoke(() => { try { vm.AudioLevel = level; } catch { } });
                        }
                        else
                        {
                            try { vm.AudioLevel = level; } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error updating audio level: {ex}");
                }
            };

            // raw PCM handler: compute peak and coalesce for UI updates
            _audioCapture.RawAudioAvailable += (s, a) =>
            {
                try
                {
                    // First audio buffer of the session: the mic is now truly live, so switch the
                    // pill from the "Starting mic" preparing state to the listening waveform.
                    if (Interlocked.CompareExchange(ref _micLive, 1, 0) == 0)
                    {
                        var win = _breathingWindow;
                        if (win != null)
                        {
                            Application.Current?.Dispatcher?.Invoke(() => { try { win.ShowListening(); } catch { } });
                        }
                    }

                    short max = 0;
                    double sumSq = 0.0;
                    int samples = 0;
                    for (int i = 0; i < a.BytesRecorded; i += 2)
                    {
                        short val = (short)(a.Buffer[i] | (a.Buffer[i + 1] << 8));
                        if (Math.Abs(val) > max) max = Math.Abs(val);
                        sumSq += (double)val * (double)val;
                        samples++;
                    }
                    var normalized = Math.Min(1.0, (double)max / short.MaxValue);
                    var rms = 0.0;
                    if (samples > 0)
                    {
                        var meanSq = sumSq / samples;
                        rms = Math.Sqrt(meanSq) / short.MaxValue; // 0..1
                    }
                    lock (_waveformLock)
                    {
                        _pendingWaveformAmplitude = Math.Max(_pendingWaveformAmplitude, normalized);
                        _sessionPeakAudioLevel = Math.Max(_sessionPeakAudioLevel, normalized);
                    }

                    var now = DateTime.UtcNow;
                    if ((now - _lastWaveformUpdate).TotalMilliseconds >= _waveformThrottleMs)
                    {
                        _lastWaveformUpdate = now;
                        double amp;
                        lock (_waveformLock)
                        {
                            amp = _pendingWaveformAmplitude;
                            _pendingWaveformAmplitude = 0.0;
                        }
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            if (_overlayViewModel != null)
                            {
                                var rmsDb = 20.0 * Math.Log10(Math.Max(1e-6, rms));
                                var dbClamped = Math.Max(-90.0, Math.Min(0.0, 20.0 * Math.Log10(Math.Max(1e-6, amp))));
                                var norm = Math.Max(0.0, Math.Min(1.0, (rmsDb + 60.0) / 60.0));
                                var percept = Math.Sqrt(norm);
                                _lastWaveformDisplay = Math.Max(percept, _lastWaveformDisplay * _waveformDecay);
                                var maxBar = _overlayViewModel.MaxWaveformBarHeight > 0 ? _overlayViewModel.MaxWaveformBarHeight : 64.0;
                                var scaled = Math.Max(2.0, _lastWaveformDisplay * maxBar);
                                _overlayViewModel.LastAmplitude = amp;
                                _overlayViewModel.LastRms = rms;
                                _overlayViewModel.LastAmplitudeDb = dbClamped;
                                _overlayViewModel.LastAmplitudeScaled = scaled;
                                // scrolling: push new bar, drop oldest
                                _overlayViewModel.WaveformPoints.Add(scaled);
                                while (_overlayViewModel.WaveformPoints.Count > _overlayViewModel.MaxWaveformPoints)
                                    _overlayViewModel.WaveformPoints.RemoveAt(0);
                                // also expose normalised amplitude for other consumers (BreathingOverlay)
                                var latestNorm = Math.Max(0.0, Math.Min(1.0, percept));
                                _overlayViewModel.LatestValue = latestNorm;
                                if (_breathingWindow != null)
                                    _breathingWindow.CurrentAmplitude = latestNorm;
                            }
                        });
                    }

                    // diagnostic: log amplitude at most once per second
                    try
                    {
                        var nowLog = DateTime.UtcNow;
                        if ((nowLog - _lastRawAudioLog).TotalSeconds >= 1)
                        {
                            _lastRawAudioLog = nowLog;
                            Logger.Info($"RawAudio amplitude: {normalized:N3}, bytes: {a.BytesRecorded}");
                        }
                    }
                    catch { }
                }
                catch { }
            };
        }

        public DictationState State => _state;

        // Known non-speech tags whisper emits. Always stripped (case-insensitive). A blanket
        // [...]/(...) regex is deliberately avoided since it could eat legitimate dictation.
        // Kept as an expandable list rather than a setting.
        private static readonly string[] NonSpeechMarkers =
        {
            "[BLANK_AUDIO]", "[NOISE]", "[SOUND]", "[MUSIC]", "[LAUGHTER]",
            "(laughs)", "(laughing)", "[APPLAUSE]", "[SILENCE]",
            "(coughs)", "(sighs)", "(clears throat)", "(speaking foreign language)",
            "[Speaking in foreign language]", "(indistinct)", "[indistinct]",
            "(mouse clicking)", "(mouse click)", "(clicking)", "(keyboard clicking)", "(typing)"
        };

        // Splits text into sentences on end punctuation so each start can be capitalized.
        private static readonly Regex SentenceStartRegex =
            new(@"(^|[.!?]\s+)(\p{Ll})", RegexOptions.Compiled);

        // Cleans and normalizes a raw transcript before insertion. This is a small, deterministic,
        // allocation-light pipeline. The first stage (ANSI/marker/gutter/duplicate-line cleanup)
        // always runs; the subsequent normalization steps are individually toggleable via settings:
        // trim -> collapse double spaces -> capitalize sentences -> remove filler words.
        // Word-replacement rules (roadmap item #2) plug in after filler removal.
        private string CleanTranscript(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            var text = StripNonSpeechAndNoise(raw);

            if (_settings.PostProcessTrimWhitespace)
                text = text.Trim();

            if (_settings.PostProcessCollapseSpaces)
                text = Regex.Replace(text, @"[ \t]{2,}", " ");

            if (_settings.EnableSpokenPunctuation)
                text = ApplySpokenPunctuation(text);

            if (_settings.PostProcessCapitalizeSentences)
                text = CapitalizeSentences(text);

            if (_settings.RemoveFillerWords)
                text = RemoveFillerWords(text);

            if (_settings.EnableCustomWordReplacements)
                text = ApplyCustomWordReplacements(text);

            if (_settings.EnableCustomRemovalRules)
                text = ApplyCustomRemovalRules(text);

            return text;
        }

        // Always-on first stage: strips ANSI escapes, known non-speech markers, transcript gutters,
        // and collapses consecutive duplicate lines. Independent of the toggleable normalization steps.
        private static string StripNonSpeechAndNoise(string raw)
        {
            // Remove common ANSI escape sequences (e.g., ESC[2K)
            var noAnsi = Regex.Replace(raw, @"\x1B\[[0-9;]*[A-Za-z]", string.Empty);

            // Remove bracketed debug tags like [Start speaking], [debug]
            noAnsi = Regex.Replace(noAnsi, @"\[(debug|Start speaking)\]", string.Empty, RegexOptions.IgnoreCase);

            // Remove known non-speech markers ([BLANK_AUDIO], (laughs), etc.) - literal, case-insensitive.
            foreach (var marker in NonSpeechMarkers)
                noAnsi = Regex.Replace(noAnsi, Regex.Escape(marker), string.Empty, RegexOptions.IgnoreCase);

            // Remove common inaudible markers and transcript gutters like >>
            noAnsi = Regex.Replace(noAnsi, @">>\s*", string.Empty);
            noAnsi = Regex.Replace(noAnsi, @"\[(inaudible)\]", string.Empty, RegexOptions.IgnoreCase);

            // Replace CR/ESC sequences left over from the stream
            noAnsi = noAnsi.Replace("\r", "\n").Replace("\u001b", string.Empty);

            // Split into lines, trim each, remove empty lines and collapse consecutive duplicate lines
            var lines = noAnsi.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var cleanedLines = new System.Collections.Generic.List<string>();
            string? last = null;
            foreach (var l in lines)
            {
                var t = l.Trim();
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (last != null && string.Equals(last, t, StringComparison.OrdinalIgnoreCase)) continue;
                cleanedLines.Add(t);
                last = t;
            }

            return string.Join("\n", cleanedLines).Trim();
        }

        // Capitalizes the first letter of the text and of each sentence following end punctuation.
        private static string CapitalizeSentences(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return SentenceStartRegex.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value.ToUpperInvariant());
        }

        // Replaces enabled spoken-punctuation phrases (e.g. "comma", "new paragraph") with their
        // literal output. Matching is whole-phrase and case-insensitive; longer phrases are applied
        // first so "question mark" wins over "mark". Punctuation marks attach to the preceding word
        // (the leading space is removed), while newline tokens expand to real line breaks.
        private string ApplySpokenPunctuation(string text)
        {
            if (string.IsNullOrEmpty(text) || _settings.SpokenPunctuationRules is null) return text;

            var rules = _settings.SpokenPunctuationRules
                .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.Phrase))
                .OrderByDescending(r => r.Phrase.Trim().Length);

            foreach (var rule in rules)
            {
                var replacement = rule.Replacement switch
                {
                    VoiceTypeSettings.LineBreakToken => "\n",
                    VoiceTypeSettings.ParagraphBreakToken => "\n\n",
                    _ => rule.Replacement
                };

                var pattern = @"(?<![\w-])" + Regex.Escape(rule.Phrase.Trim()) + @"(?![\w-])";

                // Hardcoded (not user-configurable): whisper.cpp's punctuation model frequently
                // attaches a stray "?"/"."/"," right after a spoken parenthesis phrase (e.g. "...open
                // parenthesis? Can we..."), which is not something the user actually said. Swallow one
                // such trailing mark for parenthesis replacements only.
                if (replacement is "(" or ")")
                    pattern += @"[?.,]?";

                text = Regex.Replace(text, pattern, replacement.Replace("$", "$$"), RegexOptions.IgnoreCase);
            }

            // Attach punctuation to the preceding word and tidy spacing left by replacements.
            text = Regex.Replace(text, @"[ \t]+([,.;:!?)])", "$1");
            text = Regex.Replace(text, @"[ \t]{2,}", " ");
            text = Regex.Replace(text, @"[ \t]+\n", "\n");
            text = Regex.Replace(text, @"\n[ \t]+", "\n");
            return text.Trim();
        }

        // Removes configured filler words/phrases using whole-word, case-insensitive matching, then
        // tidies up the spacing/commas left behind. Multi-token phrases (e.g. "uh-huh") are supported.
        private string RemoveFillerWords(string text)
        {
            if (string.IsNullOrEmpty(text) || _settings.FillerWords is null) return text;

            foreach (var filler in _settings.FillerWords)
            {
                if (string.IsNullOrWhiteSpace(filler)) continue;
                var pattern = @"(?<![\w-])" + Regex.Escape(filler.Trim()) + @"(?![\w-])";
                text = Regex.Replace(text, pattern, string.Empty, RegexOptions.IgnoreCase);
            }

            // Collapse doubled spaces and stray leading punctuation/spaces left by removals.
            text = Regex.Replace(text, @"[ \t]{2,}", " ");
            text = Regex.Replace(text, @"\s+([,.!?])", "$1");
            return text.Trim();
        }

        // Replaces enabled custom word-replacement rules (e.g. "co pilot" -> "Copilot") with
        // their configured replacement text. Matching is whole-word/whole-phrase and
        // case-insensitive; longer phrases are applied first so multi-word rules take
        // precedence over single-word ones. The replacement is always inserted exactly as
        // authored (no case-preservation of the matched text).
        private string ApplyCustomWordReplacements(string text)
        {
            if (string.IsNullOrEmpty(text) || _settings.CustomWordReplacements is null) return text;

            var rules = _settings.CustomWordReplacements
                .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.From))
                .OrderByDescending(r => r.From.Trim().Length);

            foreach (var rule in rules)
            {
                var pattern = @"(?<![\w-])" + Regex.Escape(rule.From.Trim()) + @"(?![\w-])";
                text = Regex.Replace(text, pattern, rule.To.Replace("$", "$$"), RegexOptions.IgnoreCase);
            }

            return text;
        }

        // Removes enabled custom removal-rule phrases from the transcript. Each rule's scope
        // determines where within a sentence the phrase must appear: at the start, at the end
        // (just before the sentence-ending punctuation), or anywhere. Sentences are split on
        // end punctuation (., !, ?); matching is whole-word/whole-phrase and case-insensitive,
        // with longer phrases applied first so multi-word rules take precedence.
        private string ApplyCustomRemovalRules(string text)
        {
            if (string.IsNullOrEmpty(text) || _settings.CustomRemovalRules is null) return text;

            var rules = _settings.CustomRemovalRules
                .Where(r => r.IsEnabled && !string.IsNullOrWhiteSpace(r.Phrase))
                .OrderByDescending(r => r.Phrase.Trim().Length)
                .ToList();
            if (rules.Count == 0) return text;

            // Split into sentences, keeping the trailing end-punctuation attached to each sentence.
            var sentences = Regex.Split(text, @"(?<=[.!?])\s+");

            for (int i = 0; i < sentences.Length; i++)
            {
                var sentence = sentences[i];
                if (string.IsNullOrWhiteSpace(sentence)) continue;

                foreach (var rule in rules)
                {
                    var phrase = Regex.Escape(rule.Phrase.Trim());
                    switch (rule.Scope)
                    {
                        case RemovalScope.StartOfSentence:
                            sentence = Regex.Replace(sentence, @"^\s*" + phrase + @"(?![\w-])[,]?\s*",
                                string.Empty, RegexOptions.IgnoreCase);
                            break;
                        case RemovalScope.EndOfSentence:
                            sentence = Regex.Replace(sentence, @"(?<![\w-])\s*" + phrase + @"\s*([.!?]*)$",
                                "$1", RegexOptions.IgnoreCase);
                            break;
                        default:
                            sentence = Regex.Replace(sentence, @"(?<![\w-])" + phrase + @"(?![\w-])",
                                string.Empty, RegexOptions.IgnoreCase);
                            break;
                    }
                }

                sentences[i] = sentence.Trim();
            }

            var result = string.Join(" ", sentences.Where(s => !string.IsNullOrWhiteSpace(s)));
            result = Regex.Replace(result, @"[ \t]{2,}", " ");
            return result.Trim();
        }

        public DictationSessionController(VoiceTypeSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Logger.Info("DictationSessionController initialized.");
        }

        // Creates an input injector configured from the current settings. The insert
        // method is selectable (Clipboard paste vs. character typing) so a future
        // settings window can switch behavior without changing the session flow.
        private Infrastructure.Input.InputInjectionService CreateInputInjector()
        {
            var useCharacterTyping = string.Equals(_settings.InsertMethod, "Typing", StringComparison.OrdinalIgnoreCase);
            return new Infrastructure.Input.InputInjectionService
            {
                UseClipboardPaste = !useCharacterTyping,
                UseCharacterTyping = useCharacterTyping,
                RestoreClipboard = _settings.EnableClipboardRestore
            };
        }

        // Transcribes a recorded WAV. In Server mode this uses the long-lived whisper-server
        // (model kept loaded) for a fast single-pass result, and transparently falls back to
        // the CLI transcriber if the server is unavailable or the request fails. In all other
        // non-stream modes it uses the CLI transcriber directly.
        private async Task<VoiceType.Models.FinalTranscriptionResult> TranscribeWavAsync(string wavPath)
        {
            if (_settings.Mode == TranscriptionMode.Server && ServerClient is { IsReady: true })
            {
                var serverResult = await ServerClient.TranscribeAsync(wavPath);
                if (serverResult.Success)
                    return serverResult;

                // A timeout is not a normal failure: falling back to CLI would reload and run the
                // same (large) model and almost certainly time out or thrash again. Surface the
                // timeout directly so the user gets an accurate, actionable message.
                if (serverResult.TimedOut)
                {
                    Logger.Error($"Server transcription timed out ({serverResult.ErrorMessage}); not falling back to CLI.");
                    return serverResult;
                }

                Logger.Error($"Server transcription failed ({serverResult.ErrorMessage}); falling back to CLI.");
            }

            var transcriber = new Infrastructure.Whisper.WhisperFinalTranscriber(_settings);
            return await transcriber.TranscribeAsync(wavPath);
        }

        // Returns true when the session identified by <paramref name="generation"/> is no longer
        // the active listening session - i.e. a fast release/stop already moved it past Listening.
        private bool IsSessionSuperseded(int generation)
        {
            lock (_sync)
            {
                return generation != _sessionGeneration || _state != DictationState.Listening;
            }
        }

        /// <summary>
        /// Re-applies the configured pill color/opacity to any currently open overlay windows
        /// (compact, breathing, and standalone status pill). Safe to call from any thread; used
        /// by the Settings window to apply appearance changes live on Save.
        /// </summary>
        public void ApplyPillAppearance()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void Apply()
            {
                try
                {
                    _overlayWindow?.ApplyAppearance(_settings);
                    _breathingWindow?.ApplyAppearance(_settings);
                    _statusWindow?.ApplyAppearance(_settings);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error applying pill appearance: {ex}");
                }
            }

            if (!dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }

        // Closes the compact and breathing overlays created during session startup. Used when a
        // start is aborted because it was superseded by a fast release before the mic engaged.
        private async Task CloseStartupOverlayAsync()
        {
            var dispatcher = Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            await dispatcher.InvokeAsync(() =>
            {
                try { _overlayWindow?.Close(); }
                catch (Exception ex) { Logger.Error($"Error closing overlay window during aborted start: {ex}"); }
                _overlayWindow = null;
                _overlayViewModel = null;

                try { _breathingWindow?.Close(); }
                catch (Exception ex) { Logger.Error($"Error closing breathing overlay during aborted start: {ex}"); }
                _breathingWindow = null;
            });
        }

        public async Task StartSessionAsync(CancellationToken ct = default, IntPtr preferredForegroundHwnd = default, IntPtr preferredFocusHwnd = default, Func<bool>? isHotkeyStillHeld = null)
        {
            var starting = false;
            int myGeneration;

            lock (_sync)
            {
                if (_state == DictationState.Idle)
                {
                    _state = DictationState.Listening;
                    starting = true;
                    myGeneration = ++_sessionGeneration;
                }
                else if (_state == DictationState.Finalizing)
                {
                    // Queue a pending start request and return; StopSessionAsync will trigger it when it completes
                    _pendingStartRequest = true;
                    Logger.Info("Start requested while previous session finalizing - queued pending start request");
                    return;
                }
                else
                {
                    Logger.Info($"StartSessionAsync ignored because state is {_state}");
                    return;
                }
            }

            Logger.Info("Starting dictation session: capturing foreground window, showing overlay and preparing audio");

            RaiseStateChanged(DictationState.Listening);

            // Reset the mic-live flag so the pill starts in the "Starting mic" preparing state
            // and only switches to the listening bars once the first audio buffer arrives.
            Interlocked.Exchange(ref _micLive, 0);

            // Reset the session peak level so we can tell silence apart from an empty transcription
            // that actually contained audio (e.g. a server timeout).
            lock (_waveformLock)
            {
                _sessionPeakAudioLevel = 0.0;
            }

            // capture foreground window so we can restore focus before paste
            try
            {
                var tracker = new Infrastructure.Windowing.ForegroundWindowTracker();
                if (preferredForegroundHwnd != IntPtr.Zero)
                {
                    // Tray-toggle path: the live foreground is the shell/taskbar, so use the last
                    // real foreground window captured by the foreground monitor instead.
                    tracker.CaptureFromHandle(preferredForegroundHwnd, preferredFocusHwnd);
                }
                else
                {
                    tracker.CaptureForegroundWindow();
                }
                // store on resources for use during final commit
                Application.Current.Resources["ForegroundTracker"] = tracker;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to capture foreground window: {ex}");
            }

            // Create and show overlay on UI thread (use Application dispatcher when available)
            var uiDispatcher = Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
            await uiDispatcher.InvokeAsync(() =>
            {
                _overlayViewModel = new UI.ViewModels.FloatingOverlayViewModel
                {
                    IsVisible = true,
                    StatusText = "Listening...",
                    PreviewText = string.Empty,
                    AudioLevel = 0.0,
                    ModelName = string.IsNullOrWhiteSpace(_settings.WhisperModelPath)
                        ? string.Empty
                        : Path.GetFileNameWithoutExtension(_settings.WhisperModelPath)
                };


                _overlayWindow = new UI.CompactOverlayWindow
                {
                    DataContext = _overlayViewModel,
                    ShowActivated = false,
                    Topmost = true
                };
                _overlayWindow.ApplyAppearance(_settings);
                _breathingWindow = new UI.BreathingOverlayWindow
                {
                    ShowActivated = false,
                    Topmost = true
                };

                try
                {
                    _breathingWindow.Show();
                    _breathingWindow.ApplyAppearance(_settings);
                    _breathingWindow.SetModelName(_overlayViewModel?.ModelName);
                    _breathingWindow.PositionBottomCenter();
                    // Show a "Starting mic" progress state until the first real audio buffer arrives,
                    // so the user does not speak into a not-yet-ready microphone and lose the first words.
                    _breathingWindow.ShowPreparing("Starting mic");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to show breathing overlay: {ex}");
                }
                // Initialise scrolling waveform on session start
                try
                {
                    if (_overlayViewModel != null)
                    {
                        _overlayViewModel.MaxWaveformBarHeight = 64;
                        _overlayViewModel.MaxWaveformPoints = 24;
                        _overlayViewModel.WaveformPoints.Clear();
                        _overlayViewModel.WaveformPolyline.Clear();
                    }
                }
                catch { }
            });

            // A very fast hotkey tap can have already released (StopSessionAsync) while the awaits
            // above ran. In that case the session was superseded: don't start the microphone.
            // Tear down the overlay we just created and bail so we don't get stuck listening.
            if (IsSessionSuperseded(myGeneration))
            {
                Logger.Info("StartSessionAsync: session superseded before mic start (fast release) - aborting start and closing overlay.");
                await CloseStartupOverlayAsync();
                return;
            }

            // Physical-key guard for hold-to-talk: right before we engage the microphone, confirm
            // the hotkey chord is actually still held down. A very fast tap can release during the
            // async overlay setup above without the state guard catching it; checking the real key
            // state here is authoritative and prevents a stuck "toggle-like" session.
            if (isHotkeyStillHeld is not null && !isHotkeyStillHeld())
            {
                Logger.Info("StartSessionAsync: hotkey no longer physically held before mic start (fast release) - aborting start and closing overlay.");
                var releasedToIdle = false;
                lock (_sync)
                {
                    if (_state == DictationState.Listening && myGeneration == _sessionGeneration)
                    {
                        _state = DictationState.Idle;
                        releasedToIdle = true;
                    }
                }
                if (releasedToIdle)
                    RaiseStateChanged(DictationState.Idle);
                await CloseStartupOverlayAsync();
                return;
            }

            // If using Stream mode, we normally skip audio capture entirely.
            // But when in Cli mode we also record a WAV file so we can run the CLI for final transcription.
            if (_settings.Mode == TranscriptionMode.Stream)
            {
                _streamClient = new Infrastructure.Whisper.WhisperStreamClient(_settings);

                if (_overlayViewModel != null)
                {
                    // Subscribe to client's OutputLineReceived event to get live transcripts
                    _streamOutputHandler = (line) =>
                    {
                        var trimmed = line?.Trim();
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_overlayViewModel != null)
                            {
                                _overlayViewModel.PreviewText = trimmed;
                                _overlayViewModel.StatusText = "Listening...";
                            }
                        });
                    };
                    _streamErrorHandler = (errLine) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (_overlayViewModel != null)
                            {
                                // show stderr lines in StatusText for debugging
                                _overlayViewModel.StatusText = $"[debug] {errLine}";
                                // also append debug lines to PreviewText so the user can see process diagnostics
                                try
                                {
                                    var existing = _overlayViewModel.PreviewText ?? string.Empty;
                                    if (!string.IsNullOrEmpty(existing)) existing += "\n";
                                    _overlayViewModel.PreviewText = existing + $"[debug] {errLine}";
                                }
                                catch { }
                            }
                        });
                    };

                    // Subscribe each handler exactly once (assignment happens above first).
                    _streamClient.OutputLineReceived += _streamOutputHandler;
                    _streamClient.ErrorLineReceived += _streamErrorHandler;
                }

                var started = _streamClient.StartMicMode(out var startError);
                if (!started)
                {
                    Logger.Error($"Failed to start whisper stream client: {startError}");
                    // Unsubscribe if we had subscribed
                    try { if (_streamOutputHandler != null) _streamClient.OutputLineReceived -= _streamOutputHandler; } catch { }
                    try { if (_streamErrorHandler != null) _streamClient.ErrorLineReceived -= _streamErrorHandler; } catch { }
                }
                else
                {
                    // Start parallel audio capture so we can render a waveform during stream-mode.
                    // If Mode is Cli we record to a WAV for final CLI transcription;
                    // otherwise start audio capture with no output file (stream-only waveform).
                    try
                    {
                        // ensure the capture instance is initialized and subscriptions attached
                        EnsureAudioCaptureInitialized();

                        string? wavPath = null;
                        if (_settings.Mode == TranscriptionMode.Cli)
                        {
                            var tempDir = _settings.TempDirectory ?? "./temp";
                            Directory.CreateDirectory(tempDir);
                            _currentSessionFilePath = Path.Combine(tempDir, $"voicetype_session_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");
                            wavPath = _currentSessionFilePath;
                        }

                        _audioCapture.Start(wavPath);
                        Logger.Info($"Audio capture started (wavPath='{wavPath}')");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to start parallel audio capture for waveform/CLI: {ex}");
                    }
                }
            }
            if (_settings.Mode != TranscriptionMode.Stream)
            {
                try
                {
                    // For diagnostic purposes, pre-fill some synthetic waveform points so UI rendering is visible
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        if (_overlayViewModel != null)
                        {
                            _overlayViewModel.WaveformPoints.Clear();
                            for (int i = 0; i < Math.Min(8, _overlayViewModel.MaxWaveformPoints); i++)
                            {
                                _overlayViewModel.WaveformPoints.Add(4 + i);
                            }
                        }
                    });

                    // Ensure we use the same audio capture instance so RawAudioAvailable is subscribed
                    EnsureAudioCaptureInitialized();
                    var tempDir = _settings.TempDirectory ?? "./temp";
                    Directory.CreateDirectory(tempDir);
                    _currentSessionFilePath = Path.Combine(tempDir, $"voicetype_session_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");
                    _audioCapture?.Start(_currentSessionFilePath);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to start audio capture: {ex}");
                }
            }

            // Second fast-release guard: the mic is only truly running at this point. A very fast
            // hotkey tap can have already released (StopSessionAsync completing to Idle) while the
            // awaits above ran - because the mic had not started yet, that stop had nothing to tear
            // down and the capture we just started would keep running with the session already gone.
            // Detect that here (state guard plus the authoritative physical-key check) and stop the
            // capture/overlay so we don't get stuck listening.
            if (IsSessionSuperseded(myGeneration) || (isHotkeyStillHeld is not null && !isHotkeyStillHeld()))
            {
                Logger.Info("StartSessionAsync: session superseded after mic start (fast release) - stopping capture and closing overlay.");
                try
                {
                    _audioCapture?.Stop();
                    _audioCapture?.Dispose();
                    _audioCapture = null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error stopping capture after superseded start: {ex}");
                }

                try { _streamClient?.Dispose(); } catch { }
                _streamClient = null;

                // If the state guard didn't already move us past Listening (i.e. only the
                // physical-key check tripped), return to Idle so the app isn't stuck listening.
                // Only raise the state change when we actually performed the reset, so we don't
                // interfere with a concurrent StopSessionAsync finalization.
                var resetToIdle = false;
                lock (_sync)
                {
                    if (_state == DictationState.Listening && myGeneration == _sessionGeneration)
                    {
                        _state = DictationState.Idle;
                        resetToIdle = true;
                    }
                }
                if (resetToIdle)
                    RaiseStateChanged(DictationState.Idle);

                await CloseStartupOverlayAsync();
                return;
            }

            await Task.Yield();
        }

        public async Task StopSessionAsync(CancellationToken ct = default)
        {
            lock (_sync)
            {
                // If we're already finalizing, check for a queued start request and cancel it
                if (_state == DictationState.Finalizing)
                {
                    if (_pendingStartRequest)
                    {
                        _pendingStartRequest = false;
                        Logger.Info("StopSessionAsync: canceled queued pending start request while finalizing");
                        return;
                    }
                    Logger.Info($"StopSessionAsync ignored because state is {_state}");
                    return;
                }

                if (_state != DictationState.Listening && _state != DictationState.Previewing)
                {
                    Logger.Info($"StopSessionAsync ignored because state is {_state}");
                    return;
                }

                _state = DictationState.Finalizing;
            }

            Logger.Info("Stopping dictation session: running finalization (placeholder)");

            DisarmIdleAutoStop();
            RaiseStateChanged(DictationState.Finalizing);

            // Update overlay status to Processing...
            if (_overlayViewModel != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => _overlayViewModel.StatusText = "Processing...");
            }

            // Keep the breathing pill visible and switch it into a "Processing..." state so the
            // user sees activity while the (potentially slow) transcription runs.
            var processingPill = _breathingWindow;
            if (processingPill != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try { processingPill.ShowProcessing("Processing"); }
                    catch (Exception ex) { Logger.Error($"Failed to show processing state on overlay: {ex}"); }
                });
            }

            // Hide and close the compact overlay only. The breathing pill stays open in its
            // processing state until transcription completes (closed via CloseBreathingWindowAsync).
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _overlayWindow?.Close();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error closing overlay window: {ex}");
                }
                _overlayWindow = null;
                _overlayViewModel = null;
            });

            // stop audio capture
            try
            {
                _audioCapture?.Stop();
                _audioCapture?.Dispose();
                _audioCapture = null;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error stopping audio capture: {ex}");
            }
            // Run final transcription using whisper stream or final transcriber depending on settings
            try
            {
                if (_settings.Mode == TranscriptionMode.Stream)
                {
                    // If using streaming, we started a WhisperStreamClient earlier (if configured).
                    // Here we finish the stream, collect the final transcript and paste it.
                    try
                    {
                        // Attempt to finish and obtain transcription
                        if (_streamClient != null)
                        {
                            // Attempt a graceful stop for mic-mode: request stop and collect buffered output
                            var result = await _streamClient.StopAndCollectAsync();

                            // Transcription finished - close the processing pill before showing results.
                            await CloseBreathingWindowAsync();

                            if (result.Success)
                            {
                                // Clean up transcript (remove ANSI sequences, debug tags, blank lines, and consecutive duplicates)
                                var cleaned = CleanTranscript(result.Text);
                                Logger.Info($"Final transcription (stream): {cleaned}");

                                // Unsubscribe the overlay handlers if present
                                try { if (_streamOutputHandler != null) _streamClient.OutputLineReceived -= _streamOutputHandler; } catch { }
                                try { if (_streamErrorHandler != null) _streamClient.ErrorLineReceived -= _streamErrorHandler; } catch { }

                                // Update overlay with final text if present
                                try
                                {
                                    if (!string.IsNullOrWhiteSpace(cleaned) && _overlayViewModel != null)
                                    {
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            _overlayViewModel.PreviewText = cleaned;
                                            _overlayViewModel.StatusText = "Final";
                                        });
                                    }
                                }
                                catch { }

                                await InsertTextOrNotifyAsync(cleaned, result.Text);

                                // If result.Text is empty, show buffered output for debugging
                                if (string.IsNullOrWhiteSpace(cleaned))
                                {
                                    try
                                    {
                                        var (stdout, stderr) = _streamClient.GetBufferedOutput();
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            if (_overlayViewModel != null)
                                            {
                                                _overlayViewModel.PreviewText = "[debug stdout]\n" + stdout + "\n[debug stderr]\n" + stderr;
                                                _overlayViewModel.StatusText = "No transcript (see debug)";
                                            }
                                        });
                                    }
                                    catch { }
                                }
                            }
                            else
                            {
                                Logger.Error($"Stream transcription failed: {result.ErrorMessage}");
                                ShowNotification("Transcription failed. Please try again.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error finalizing whisper stream: {ex}");
                    }
                    finally
                    {
                        try { _streamClient?.Dispose(); } catch { }
                        _streamClient = null;
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(_currentSessionFilePath))
                    {
                        var result = await TranscribeWavAsync(_currentSessionFilePath);

                        // Transcription finished - close the processing pill before showing results.
                        await CloseBreathingWindowAsync();

                        if (result.Success)
                        {
                            Logger.Info($"Final transcription: {result.Text}");

                            // Clean the raw transcript (strips ANSI, gutters, and debug tags like
                            // [BLANK_AUDIO]) so silence collapses to empty and shows the "no speech"
                            // pill instead of typing the marker at the cursor.
                            var finalText = CleanTranscript(result.Text ?? string.Empty);
                            await InsertTextOrNotifyAsync(finalText, result.Text);
                        }
                        else if (result.TimedOut)
                        {
                            // Honest, actionable message: a timeout can have several causes, so we
                            // don't claim it's memory, but we suggest trying a different model if it
                            // keeps happening. Shown longer so the user has time to read it.
                            Logger.Error($"Transcription timed out: {result.ErrorMessage}");
                            ShowNotification(
                                "Transcription timed out. If this keeps happening, try a smaller Whisper model.",
                                autoCloseMs: 9000);
                        }
                        else
                        {
                            Logger.Error($"Transcription failed: {result.ErrorMessage}");
                            ShowNotification("Transcription failed. Please try again.");
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error($"Error during final transcription: {ex}");
            }

            // Defensive: ensure the processing pill is closed even if no transcription path ran.
            await CloseBreathingWindowAsync();

            // Clean up temporary WAV file if it was created for this session to avoid filling disk
            try
            {
                if (!string.IsNullOrWhiteSpace(_currentSessionFilePath) && File.Exists(_currentSessionFilePath))
                {
                    try
                    {
                        File.Delete(_currentSessionFilePath);
                        Logger.Info($"Deleted temporary WAV: {_currentSessionFilePath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to delete temporary WAV '{_currentSessionFilePath}': {ex.Message}");
                    }
                }
            }
            catch { }
            finally
            {
                _currentSessionFilePath = null;
            }

            var startPending = false;
            lock (_sync)
            {
                _state = DictationState.Idle;
                if (_pendingStartRequest)
                {
                    startPending = true;
                    _pendingStartRequest = false;
                }
            }

            Logger.Info("Dictation session ended and state set to Idle.");

            RaiseStateChanged(DictationState.Idle);

            if (startPending)
            {
                Logger.Info("Processing queued start request after finalization.");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await StartSessionAsync();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error starting queued session: {ex}");
                    }
                });
            }
        }

        /// <summary>
        /// Closes the breathing pill overlay on the UI thread, if it is still open.
        /// Safe to call multiple times.
        /// </summary>
        private async Task CloseBreathingWindowAsync()
        {
            var win = _breathingWindow;
            if (win == null) return;
            _breathingWindow = null;
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try { win.Close(); }
                    catch (Exception ex) { Logger.Error($"Error closing breathing overlay: {ex}"); }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error dispatching breathing overlay close: {ex}");
            }
        }

        /// <summary>
        /// Restores the original foreground window and verifies it actually regained focus
        /// before inserting text. If focus cannot be confirmed, the text is left on the
        /// clipboard and a non-intrusive notification tells the user it was copied.
        /// </summary>
        /// <param name="text">The post-processed ("Final text") transcript to insert.</param>
        /// <param name="rawText">
        /// The raw ("You spoke") transcript before <see cref="CleanTranscript"/>, used to record a
        /// comparison-history entry and show the post-insertion bulb when it differs from
        /// <paramref name="text"/>. Pass null when unavailable (comparison/bulb features are skipped).
        /// </param>
        private async Task InsertTextOrNotifyAsync(string text, string? rawText = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                double peak;
                lock (_waveformLock)
                {
                    peak = _sessionPeakAudioLevel;
                }

                // Distinguish genuine silence from an empty result that still had audio (e.g. a
                // transcription timeout or failure). "No speech recognized" should only appear when
                // the microphone captured little or no sound.
                if (peak >= SpeechDetectedPeakThreshold)
                {
                    Logger.Info($"InsertTextOrNotifyAsync: no text to insert, but audio was present (peak={peak:N3}).");
                    ShowNotification("Could not transcribe the audio. Please try again.");
                }
                else
                {
                    Logger.Info($"InsertTextOrNotifyAsync: no text to insert and no speech detected (peak={peak:N3}).");
                    ShowNotification("No speech recognized. Please move closer to the mic and try again.");
                }
                return;
            }

            Infrastructure.Windowing.ForegroundWindowTracker? tracker = null;
            if (Application.Current.Resources.Contains("ForegroundTracker") &&
                Application.Current.Resources["ForegroundTracker"] is Infrastructure.Windowing.ForegroundWindowTracker ft)
            {
                tracker = ft;
            }

            // Restore the previously-active window and confirm it is really the foreground.
            var focusRestored = tracker != null && await tracker.RestoreAndVerifyAsync();

            if (focusRestored)
            {
                // Gate insertion on whether the restored target actually accepts text. Pasting or
                // typing into a non-editable surface would silently lose the transcript, so when no
                // editable control is focused we fall back to leaving it on the clipboard.
                if (_settings.CopyToClipboardWhenNoEditable
                    && !Infrastructure.Windowing.FocusedControlInspector.IsEditableControlFocused())
                {
                    Logger.Info("InsertTextOrNotifyAsync: no editable control focused; leaving transcript on clipboard.");
                    try
                    {
                        await Infrastructure.Input.ClipboardHelper.SetTextAsync(text);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to copy text to clipboard for fallback: {ex}");
                    }

                    if (_settings.ShowClipboardCopyNotification)
                    {
                        ShowClipboardFallbackNotification(text);
                    }
                    return;
                }

                var injector = CreateInputInjector();
                try
                {
                    var inserted = await injector.InsertTextAsync(text);
                    if (inserted)
                    {
                        Logger.Info("Text inserted into target application.");
                        RecordComparisonAndShowBulb(rawText, text);
                    }
                    else Logger.Error("Failed to insert text into target application.");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error inserting text: {ex}");
                }
                return;
            }

            // Focus could not be confirmed: keep the text on the clipboard so the user can
            // paste it manually, and show a non-intrusive notification.
            Logger.Error("Could not confirm the target window focus. Leaving text on the clipboard and notifying the user.");
            try
            {
                await Infrastructure.Input.ClipboardHelper.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to copy text to clipboard for fallback: {ex}");
            }

            ShowClipboardFallbackNotification(text);
        }

        /// <summary>
        /// Builds a comparison entry for the just-inserted text and persists it to
        /// <see cref="HistoryService"/> (updating any open history window immediately) and
        /// publishes it to <see cref="PreviewState"/>, regardless of whether post-processing
        /// changed anything. The post-insertion bulb is only shown when post-processing actually
        /// altered the text, since there is nothing meaningful to compare otherwise. No-ops
        /// silently when the raw transcript is unavailable, or when history/preview services were
        /// never wired up by the host application.
        /// </summary>
        private void RecordComparisonAndShowBulb(string? rawText, string finalText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return;

            var textChanged = !string.Equals(rawText, finalText, StringComparison.Ordinal);

            try
            {
                var (spokenHighlights, finalHighlights) = textChanged
                    ? TranscriptDiffService.BuildHighlights(rawText, finalText)
                    : (new List<HighlightSpan>(), new List<HighlightSpan>());

                var entry = new ComparisonEntry(
                    Guid.NewGuid(),
                    DateTime.UtcNow,
                    rawText,
                    finalText,
                    spokenHighlights,
                    finalHighlights,
                    CurrentModelDisplayName);

                if (_settings.EnableTranscriptHistory)
                    HistoryService?.AddEntry(entry);
                PreviewState?.SetCurrent(entry);

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    UI.ComparisonWindow.NotifyNewEntry(entry);
                    if (textChanged)
                        ShowTranscriptBulb(entry);
                }));
            }
            catch (Exception ex)
            {
                Logger.Error($"Error recording transcript comparison: {ex}");
            }
        }

        private UI.TranscriptBulbWindow? _transcriptBulb;

        // Shows the post-insertion bulb near the current mouse cursor. Must run on the UI thread.
        private void ShowTranscriptBulb(ComparisonEntry entry)
        {
            try
            {
                try { _transcriptBulb?.Close(); } catch { }

                var targetHwnd = Native.GetForegroundWindow();
                Native.GetCursorPos(out var cursorPos);

                var bulb = new UI.TranscriptBulbWindow(targetHwnd) { ShowActivated = false };
                bulb.PositionNear(new System.Drawing.Point(cursorPos.X, cursorPos.Y));
                bulb.BulbClicked += (_, _) =>
                {
                    try
                    {
                        var existing = UI.ComparisonWindow.GetOpenWindow();
                        if (existing is not null)
                        {
                            existing.Activate();
                        }
                        else
                        {
                            var popup = new UI.ComparisonWindow { ShowActivated = true, HistoryService = HistoryService };
                            popup.LoadEntries(HistoryService?.GetEntries() ?? new[] { entry });
                            popup.PositionNear(new Point(cursorPos.X, cursorPos.Y));
                            popup.Show();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error opening comparison popup: {ex}");
                    }
                    finally
                    {
                        try { bulb.Close(); } catch { }
                    }
                };
                bulb.Show();
                _transcriptBulb = bulb;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error showing transcript bulb: {ex}");
            }
        }

        // Minimal native helpers for cursor/foreground-window queries used by the transcript bulb.
        private static class Native
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool GetCursorPos(out POINT lpPoint);

            public struct POINT { public int X; public int Y; }
        }

        /// <summary>
        /// Shows a self-dismissing pill notification telling the user the transcript was
        /// copied to the clipboard (used when focus restoration could not be confirmed).
        /// </summary>
        private void ShowClipboardFallbackNotification(string text)
        {
            var preview = text.Length > 60 ? text.Substring(0, 57) + "..." : text;
            ShowNotification($"Copied to clipboard: \"{preview}\"  (press Ctrl+V to paste)");
        }

        /// <summary>
        /// Shows a self-dismissing pill notification with the given message on the UI thread.
        /// Used for clipboard-fallback and empty-transcription notices.
        /// </summary>
        private void ShowNotification(string message, int autoCloseMs = 5000)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var window = new UI.BreathingOverlayWindow
                        {
                            ShowActivated = false,
                            Topmost = true
                        };
                        window.Show();
                        window.ApplyAppearance(_settings);
                        window.ShowMessage(message, autoCloseMs);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to show notification: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Error dispatching notification: {ex}");
            }
        }

        public void Dispose()
        {
            DisarmIdleAutoStop();

            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _overlayWindow?.Close();
                    _breathingWindow?.Close();
                    _statusWindow?.Close();
                    _overlayWindow = null;
                    _breathingWindow = null;
                    _statusWindow = null;
                    _overlayViewModel = null;
                });
            }
            catch { }
        }
    }
}
