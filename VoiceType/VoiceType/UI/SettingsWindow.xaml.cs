using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NAudio.Wave;
using VoiceType.Core;
using VoiceType.Infrastructure.Config;
using VoiceType.Infrastructure.Logging;
using VoiceType.Infrastructure.Whisper;

namespace VoiceType.UI
{
    /// <summary>
    /// Settings window: lets the user pick the model, microphone, transcription mode and hotkey,
    /// then persists the changes to appsettings.json via <see cref="SettingsLoader"/>. Shown
    /// single-instance: <see cref="ShowSingleInstance"/> reuses and focuses an existing instance
    /// instead of opening a second one.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private static SettingsWindow? _instance;

        // The shared settings singleton (mutated in place so all consumers see changes).
        private readonly VoiceTypeSettings _settings;

        // Pending hotkey captured via the press-to-capture box, normalised to the string form
        // GlobalHotkeyManager expects (modifiers joined by '+', key = a System.Windows.Input.Key name).
        private string _capturedModifiers = string.Empty;
        private string _capturedKey = string.Empty;

        // Snapshot of the captured combo taken when the box gains focus, so Esc can revert to it.
        private string _preCaptureModifiers = string.Empty;
        private string _preCaptureKey = string.Empty;

        // Pending toggle-mode hotkey captured via its own press-to-capture box, in the same
        // normalised string form (modifiers joined by '+', key = a Key name).
        private string _capturedToggleModifiers = string.Empty;
        private string _capturedToggleKey = string.Empty;
        private string _preCaptureToggleModifiers = string.Empty;
        private string _preCaptureToggleKey = string.Empty;

        private sealed record ModelItem(string Path, string DisplayName);
        private sealed record MicItem(int Index, string Name);

        public SettingsWindow()
        {
            InitializeComponent();

            _settings = Application.Current?.Resources["Settings"] as VoiceTypeSettings
                        ?? SettingsLoader.Load();

            PopulateControls();
        }

        private void PopulateControls()
        {
            // Models.
            var models = new List<ModelItem>();
            try
            {
                foreach (var path in new WhisperProcessRunner(_settings).EnumerateModels())
                    models.Add(new ModelItem(path, Path.GetFileNameWithoutExtension(path)));
            }
            catch (Exception ex)
            {
                Logger.Error($"SettingsWindow: failed to enumerate models: {ex.Message}");
            }

            // Ensure the currently configured model is present even if it lives outside the folder.
            if (!string.IsNullOrWhiteSpace(_settings.WhisperModelPath) &&
                !models.Exists(m => string.Equals(Path.GetFileName(m.Path), Path.GetFileName(_settings.WhisperModelPath), StringComparison.OrdinalIgnoreCase)))
            {
                models.Insert(0, new ModelItem(_settings.WhisperModelPath, Path.GetFileNameWithoutExtension(_settings.WhisperModelPath)));
            }

            ModelComboBox.ItemsSource = models;
            var activeName = string.IsNullOrEmpty(_settings.WhisperModelPath) ? null : Path.GetFileName(_settings.WhisperModelPath);
            ModelComboBox.SelectedItem = activeName is null
                ? null
                : models.Find(m => string.Equals(Path.GetFileName(m.Path), activeName, StringComparison.OrdinalIgnoreCase));

            // Microphones.
            var mics = new List<MicItem>();
            try
            {
                for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                    mics.Add(new MicItem(i, WaveInEvent.GetCapabilities(i).ProductName));
            }
            catch (Exception ex)
            {
                Logger.Error($"SettingsWindow: failed to enumerate microphones: {ex.Message}");
            }

            if (mics.Count == 0)
                mics.Add(new MicItem(0, "Default microphone"));

            MicrophoneComboBox.ItemsSource = mics;
            MicrophoneComboBox.SelectedItem = mics.Find(m => m.Index == _settings.MicrophoneDeviceIndex) ?? mics[0];

            // Transcription mode.
            ModeComboBox.ItemsSource = Enum.GetValues(typeof(TranscriptionMode));
            ModeComboBox.SelectedItem = _settings.Mode;

            // Hotkey.
            _capturedModifiers = _settings.HotkeyModifiers ?? string.Empty;
            _capturedKey = _settings.HotkeyKey ?? string.Empty;
            UpdateHotkeyDisplay();

            // Toggle-mode hotkey.
            _capturedToggleModifiers = _settings.ToggleHotkeyModifiers ?? string.Empty;
            _capturedToggleKey = _settings.ToggleHotkeyKey ?? string.Empty;
            UpdateToggleHotkeyDisplay();

            // General fields.
            LanguageTextBox.Text = _settings.Language ?? string.Empty;

            InsertMethodComboBox.ItemsSource = new[] { "Clipboard", "Typing" };
            InsertMethodComboBox.SelectedItem =
                string.Equals(_settings.InsertMethod, "Typing", StringComparison.OrdinalIgnoreCase) ? "Typing" : "Clipboard";

            EnableClipboardRestoreCheckBox.IsChecked = _settings.EnableClipboardRestore;
            TempDirectoryTextBox.Text = _settings.TempDirectory ?? string.Empty;
            PreviewChunkTextBox.Text = _settings.PreviewChunkMilliseconds.ToString(CultureInfo.InvariantCulture);
            PreviewThrottleTextBox.Text = _settings.PreviewThrottleMilliseconds.ToString(CultureInfo.InvariantCulture);

            UseTrayIconToggleCheckBox.IsChecked = _settings.UseTrayIconToggle;
            ToggleIdleAutoStopCheckBox.IsChecked = _settings.ToggleIdleAutoStopEnabled;
            ToggleIdleAutoStopSecondsTextBox.Text = _settings.ToggleIdleAutoStopSeconds.ToString(CultureInfo.InvariantCulture);

            CopyToClipboardWhenNoEditableCheckBox.IsChecked = _settings.CopyToClipboardWhenNoEditable;
            ShowClipboardCopyNotificationCheckBox.IsChecked = _settings.ShowClipboardCopyNotification;

            // Server fields.
            ServerExecutableTextBox.Text = _settings.WhisperServerExecutablePath ?? string.Empty;
            ServerHostTextBox.Text = _settings.WhisperServerHost ?? string.Empty;
            ServerPortTextBox.Text = _settings.WhisperServerPort.ToString(CultureInfo.InvariantCulture);
            ServerTimeoutTextBox.Text = _settings.WhisperServerTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            ServerArgumentsTextBox.Text = _settings.WhisperServerArguments ?? string.Empty;

            // Stream fields.
            StreamExecutableTextBox.Text = _settings.WhisperStreamExecutablePath ?? string.Empty;
            StreamArgumentsTextBox.Text = _settings.WhisperStreamArguments ?? string.Empty;

            // CLI fields.
            CliExecutableTextBox.Text = _settings.WhisperCliExecutablePath ?? string.Empty;

            UpdateModeSectionsVisibility(_settings.Mode);
        }

        /// <summary>
        /// Shows only the mode-specific section (Server/Stream/CLI) that matches the selected
        /// transcription mode. WavFile has no dedicated executable settings of its own.
        /// </summary>
        private void UpdateModeSectionsVisibility(TranscriptionMode mode)
        {
            ServerGroupBox.Visibility = mode == TranscriptionMode.Server ? Visibility.Visible : Visibility.Collapsed;
            StreamGroupBox.Visibility = mode == TranscriptionMode.Stream ? Visibility.Visible : Visibility.Collapsed;
            CliGroupBox.Visibility = mode == TranscriptionMode.Cli ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModeComboBox.SelectedItem is TranscriptionMode mode)
                UpdateModeSectionsVisibility(mode);
        }

        /// <summary>
        /// Renders the currently captured hotkey combo into the read-only capture box.
        /// </summary>
        private void UpdateHotkeyDisplay()
        {
            string combo;
            if (string.IsNullOrWhiteSpace(_capturedModifiers))
            {
                combo = _capturedKey;
            }
            else
            {
                var mods = string.Join(" + ", _capturedModifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                combo = string.IsNullOrWhiteSpace(_capturedKey) ? mods : $"{mods} + {_capturedKey}";
            }

            HotkeyCaptureTextBox.Text = string.IsNullOrWhiteSpace(combo) ? "(none)" : combo;
        }

        private void HotkeyCaptureTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Remember the current combo so Esc can restore it if capture is abandoned.
            _preCaptureModifiers = _capturedModifiers;
            _preCaptureKey = _capturedKey;
            ClearHotkeyValidation();
            HotkeyCaptureTextBox.Text = "Press a key combination...";
        }

        private void HotkeyCaptureTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateHotkeyDisplay();
        }

        /// <summary>
        /// Renders the currently captured toggle-mode hotkey combo into its read-only capture box.
        /// </summary>
        private void UpdateToggleHotkeyDisplay()
        {
            string combo;
            if (string.IsNullOrWhiteSpace(_capturedToggleModifiers))
            {
                combo = _capturedToggleKey;
            }
            else
            {
                var mods = string.Join(" + ", _capturedToggleModifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                combo = string.IsNullOrWhiteSpace(_capturedToggleKey) ? mods : $"{mods} + {_capturedToggleKey}";
            }

            ToggleHotkeyCaptureTextBox.Text = string.IsNullOrWhiteSpace(combo) ? "(none)" : combo;
        }

        private void ToggleHotkeyCaptureTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _preCaptureToggleModifiers = _capturedToggleModifiers;
            _preCaptureToggleKey = _capturedToggleKey;
            ClearToggleHotkeyValidation();
            ToggleHotkeyCaptureTextBox.Text = "Press a key combination...";
        }

        private void ToggleHotkeyCaptureTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateToggleHotkeyDisplay();
        }

        /// <summary>
        /// Captures the pressed key combination for the toggle-mode hotkey, mirroring the
        /// dictation capture logic in <see cref="HotkeyCaptureTextBox_PreviewKeyDown"/>.
        /// </summary>
        private void ToggleHotkeyCaptureTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key is Key.Tab)
                return;

            e.Handled = true;

            if (key == Key.Escape)
            {
                _capturedToggleModifiers = _preCaptureToggleModifiers;
                _capturedToggleKey = _preCaptureToggleKey;
                ClearToggleHotkeyValidation();
                UpdateToggleHotkeyDisplay();
                Keyboard.ClearFocus();
                return;
            }

            bool keyIsCtrl = key is Key.LeftCtrl or Key.RightCtrl;
            bool keyIsAlt = key is Key.LeftAlt or Key.RightAlt;
            bool keyIsShift = key is Key.LeftShift or Key.RightShift;
            bool keyIsWin = key is Key.LWin or Key.RWin;
            bool keyIsModifier = keyIsCtrl || keyIsAlt || keyIsShift || keyIsWin;

            var mods = new List<string>();
            var held = Keyboard.Modifiers;
            if (held.HasFlag(ModifierKeys.Control) && !keyIsCtrl) mods.Add("Ctrl");
            if (held.HasFlag(ModifierKeys.Shift) && !keyIsShift) mods.Add("Shift");
            if (held.HasFlag(ModifierKeys.Alt) && !keyIsAlt) mods.Add("Alt");
            if (held.HasFlag(ModifierKeys.Windows) && !keyIsWin) mods.Add("Win");

            if (keyIsModifier)
            {
                var pressed = keyIsCtrl ? "Ctrl" : keyIsAlt ? "Alt" : keyIsShift ? "Shift" : "Win";
                var running = new List<string>(mods) { pressed };
                ToggleHotkeyCaptureTextBox.Text = string.Join(" + ", running) + " + ...";
                return;
            }

            _capturedToggleModifiers = string.Join("+", mods);
            _capturedToggleKey = key.ToString();
            ClearToggleHotkeyValidation();

            ToggleHotkeyCaptureTextBox.Text = mods.Count > 0
                ? string.Join(" + ", mods) + " + " + _capturedToggleKey
                : _capturedToggleKey;
        }

        /// <summary>
        /// Validates the captured toggle-mode hotkey combo, mirroring <see cref="ValidateCapturedHotkey"/>.
        /// </summary>
        private bool ValidateCapturedToggleHotkey()
        {
            if (string.IsNullOrWhiteSpace(_capturedToggleKey))
            {
                ShowToggleHotkeyValidation("Press a main key (e.g. a letter or F-key) in addition to any modifiers.");
                return false;
            }

            if (_capturedToggleKey is "LeftCtrl" or "RightCtrl" or "LeftAlt" or "RightAlt"
                or "LeftShift" or "RightShift" or "LWin" or "RWin")
            {
                if (string.IsNullOrWhiteSpace(_capturedToggleModifiers))
                {
                    ShowToggleHotkeyValidation("A modifier alone is not a valid hotkey; add a main key.");
                    return false;
                }
            }

            if (!Enum.TryParse<Key>(_capturedToggleKey, out _))
            {
                ShowToggleHotkeyValidation("The captured key is not recognised; try a different combination.");
                return false;
            }

            ClearToggleHotkeyValidation();
            return true;
        }

        private void ShowToggleHotkeyValidation(string message)
        {
            ToggleHotkeyValidationText.Text = message;
            ToggleHotkeyValidationText.Visibility = Visibility.Visible;
            ToggleHotkeyHintText.Visibility = Visibility.Collapsed;
        }

        private void ClearToggleHotkeyValidation()
        {
            ToggleHotkeyValidationText.Visibility = Visibility.Collapsed;
            ToggleHotkeyValidationText.Text = string.Empty;
            ToggleHotkeyHintText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Captures the pressed key combination and normalises it into modifier/key strings that
        /// <see cref="Infrastructure.Hotkeys.GlobalHotkeyManager"/> accepts. When the main key is
        /// itself a modifier (the default hotkey uses LeftAlt), it is recorded as the key and the
        /// remaining held modifiers become the modifier set.
        /// </summary>
        private void HotkeyCaptureTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Tab moves focus normally so keyboard navigation keeps working.
            if (key is Key.Tab)
                return;

            e.Handled = true;

            // Escape abandons capture and restores the previously captured combo.
            if (key == Key.Escape)
            {
                _capturedModifiers = _preCaptureModifiers;
                _capturedKey = _preCaptureKey;
                ClearHotkeyValidation();
                UpdateHotkeyDisplay();
                Keyboard.ClearFocus();
                return;
            }

            bool keyIsCtrl = key is Key.LeftCtrl or Key.RightCtrl;
            bool keyIsAlt = key is Key.LeftAlt or Key.RightAlt;
            bool keyIsShift = key is Key.LeftShift or Key.RightShift;
            bool keyIsWin = key is Key.LWin or Key.RWin;
            bool keyIsModifier = keyIsCtrl || keyIsAlt || keyIsShift || keyIsWin;

            var mods = new List<string>();
            var held = Keyboard.Modifiers;
            if (held.HasFlag(ModifierKeys.Control) && !keyIsCtrl) mods.Add("Ctrl");
            if (held.HasFlag(ModifierKeys.Shift) && !keyIsShift) mods.Add("Shift");
            if (held.HasFlag(ModifierKeys.Alt) && !keyIsAlt) mods.Add("Alt");
            if (held.HasFlag(ModifierKeys.Windows) && !keyIsWin) mods.Add("Win");

            // While only modifier keys are held (combo in progress), show the running combo but
            // don't commit it yet - a modifier alone is not a valid final hotkey. Wait for a
            // non-modifier main key to be pressed before recording the captured value.
            if (keyIsModifier)
            {
                var pressed = keyIsCtrl ? "Ctrl" : keyIsAlt ? "Alt" : keyIsShift ? "Shift" : "Win";
                var running = new List<string>(mods) { pressed };
                HotkeyCaptureTextBox.Text = string.Join(" + ", running) + " + ...";
                return;
            }

            // A non-modifier main key completes the combo. key.ToString() always yields a valid
            // Key enum name, which GlobalHotkeyManager parses via Enum.TryParse<Key>.
            _capturedModifiers = string.Join("+", mods);
            _capturedKey = key.ToString();
            ClearHotkeyValidation();

            HotkeyCaptureTextBox.Text = mods.Count > 0
                ? string.Join(" + ", mods) + " + " + _capturedKey
                : _capturedKey;
        }

        /// <summary>
        /// Validates the captured hotkey combo. A valid combo requires a non-modifier main key
        /// (a lone modifier is rejected) whose name parses to a <see cref="Key"/>. Shows an inline
        /// message on failure so an invalid value is never persisted.
        /// </summary>
        private bool ValidateCapturedHotkey()
        {
            if (string.IsNullOrWhiteSpace(_capturedKey))
            {
                ShowHotkeyValidation("Press a main key (e.g. a letter or F-key) in addition to any modifiers.");
                return false;
            }

            if (_capturedKey is "LeftCtrl" or "RightCtrl" or "LeftAlt" or "RightAlt"
                or "LeftShift" or "RightShift" or "LWin" or "RWin")
            {
                // A modifier as the "main key" is only valid as part of a multi-key combo
                // (e.g. Ctrl+LeftAlt for push-to-talk); a lone modifier is not a hotkey.
                if (string.IsNullOrWhiteSpace(_capturedModifiers))
                {
                    ShowHotkeyValidation("A modifier alone is not a valid hotkey; add a main key.");
                    return false;
                }
            }

            if (!Enum.TryParse<Key>(_capturedKey, out _))
            {
                ShowHotkeyValidation("The captured key is not recognised; try a different combination.");
                return false;
            }

            ClearHotkeyValidation();
            return true;
        }

        private void ShowHotkeyValidation(string message)
        {
            HotkeyValidationText.Text = message;
            HotkeyValidationText.Visibility = Visibility.Visible;
            HotkeyHintText.Visibility = Visibility.Collapsed;
        }

        private void ClearHotkeyValidation()
        {
            HotkeyValidationText.Visibility = Visibility.Collapsed;
            HotkeyValidationText.Text = string.Empty;
            HotkeyHintText.Visibility = Visibility.Visible;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate numeric fields before mutating settings so a bad value can't be persisted.
            if (!TryParsePositiveInt(PreviewChunkTextBox.Text, "Preview chunk (ms)", out var previewChunk) ||
                !TryParsePositiveInt(PreviewThrottleTextBox.Text, "Preview throttle (ms)", out var previewThrottle) ||
                !TryParsePositiveInt(ToggleIdleAutoStopSecondsTextBox.Text, "Idle timeout (seconds)", out var idleSeconds) ||
                !TryParsePositiveInt(ServerTimeoutTextBox.Text, "Server timeout (s)", out var serverTimeout) ||
                !TryParsePort(ServerPortTextBox.Text, out var serverPort))
            {
                return;
            }

            // Reject an invalid hotkey (e.g. a lone modifier or unrecognised key) before saving.
            if (!ValidateCapturedHotkey())
            {
                return;
            }

            // Reject an invalid toggle-mode hotkey before saving.
            if (!ValidateCapturedToggleHotkey())
            {
                return;
            }

            var previousModel = _settings.WhisperModelPath;
            var previousMode = _settings.Mode;
            var previousHotkey = _settings.DictationHotkey ?? string.Empty;
            var previousToggleHotkey = _settings.ToggleHotkey ?? string.Empty;
            var previousMic = _settings.MicrophoneDeviceIndex;

            // Snapshot server-launch fields so we can detect whether a restart is needed.
            var previousServerExe = _settings.WhisperServerExecutablePath ?? string.Empty;
            var previousServerHost = _settings.WhisperServerHost ?? string.Empty;
            var previousServerPort = _settings.WhisperServerPort;
            var previousServerArgs = _settings.WhisperServerArguments ?? string.Empty;

            if (ModelComboBox.SelectedItem is ModelItem model)
                _settings.WhisperModelPath = model.Path;
            if (MicrophoneComboBox.SelectedItem is MicItem mic)
                _settings.MicrophoneDeviceIndex = mic.Index;
            if (ModeComboBox.SelectedItem is TranscriptionMode mode)
                _settings.Mode = mode;
            _settings.DictationHotkey = VoiceTypeSettings.CombineHotkey(_capturedModifiers, _capturedKey);
            _settings.ToggleHotkey = VoiceTypeSettings.CombineHotkey(_capturedToggleModifiers, _capturedToggleKey);

            // General fields.
            _settings.Language = LanguageTextBox.Text?.Trim() ?? string.Empty;
            _settings.InsertMethod = InsertMethodComboBox.SelectedItem as string ?? "Clipboard";
            _settings.EnableClipboardRestore = EnableClipboardRestoreCheckBox.IsChecked == true;
            _settings.TempDirectory = TempDirectoryTextBox.Text?.Trim() ?? string.Empty;
            _settings.PreviewChunkMilliseconds = previewChunk;
            _settings.PreviewThrottleMilliseconds = previewThrottle;

            // Tray toggle mode fields.
            _settings.UseTrayIconToggle = UseTrayIconToggleCheckBox.IsChecked == true;
            _settings.ToggleIdleAutoStopEnabled = ToggleIdleAutoStopCheckBox.IsChecked == true;
            _settings.ToggleIdleAutoStopSeconds = idleSeconds;

            _settings.CopyToClipboardWhenNoEditable = CopyToClipboardWhenNoEditableCheckBox.IsChecked == true;
            _settings.ShowClipboardCopyNotification = ShowClipboardCopyNotificationCheckBox.IsChecked == true;

            // Server fields.
            _settings.WhisperServerExecutablePath = ServerExecutableTextBox.Text?.Trim() ?? string.Empty;
            _settings.WhisperServerHost = ServerHostTextBox.Text?.Trim() ?? string.Empty;
            _settings.WhisperServerPort = serverPort;
            _settings.WhisperServerTimeoutSeconds = serverTimeout;
            _settings.WhisperServerArguments = ServerArgumentsTextBox.Text?.Trim() ?? string.Empty;

            // Stream fields.
            _settings.WhisperStreamExecutablePath = StreamExecutableTextBox.Text?.Trim() ?? string.Empty;
            _settings.WhisperStreamArguments = StreamArgumentsTextBox.Text?.Trim() ?? string.Empty;

            // CLI fields.
            _settings.WhisperCliExecutablePath = CliExecutableTextBox.Text?.Trim() ?? string.Empty;

            try
            {
                await SettingsLoader.SaveAsync(_settings);
            }
            catch (Exception ex)
            {
                Logger.Error($"SettingsWindow: failed to save settings: {ex}");
                MessageBox.Show(this, $"Could not save settings: {ex.Message}", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Determine which settings changed so each can be applied live without an app restart.
            var modelChanged = !string.Equals(previousModel, _settings.WhisperModelPath, StringComparison.OrdinalIgnoreCase);
            var modeChanged = previousMode != _settings.Mode;
            var hotkeyChanged = !string.Equals(previousHotkey, _settings.DictationHotkey, StringComparison.OrdinalIgnoreCase);
            var toggleHotkeyChanged = !string.Equals(previousToggleHotkey, _settings.ToggleHotkey, StringComparison.OrdinalIgnoreCase);
            var serverLaunchChanged =
                !string.Equals(previousServerExe, _settings.WhisperServerExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previousServerHost, _settings.WhisperServerHost, StringComparison.OrdinalIgnoreCase) ||
                previousServerPort != _settings.WhisperServerPort ||
                !string.Equals(previousServerArgs, _settings.WhisperServerArguments, StringComparison.Ordinal);

            var controller = Application.Current?.Resources["DictationController"] as DictationSessionController;
            var app = Application.Current as App;

            // Mode change: create/dispose the whisper-server and re-wire the controller. When the
            // new mode is Server, ApplyModeAsync starts the server with the (already-updated)
            // settings, so a simultaneous model/launch change needs no separate restart.
            if (modeChanged && app is not null)
            {
                controller?.ShowStatusPill("Applying mode");
                try { await app.ApplyModeAsync(_settings.Mode); }
                catch (Exception ex) { Logger.Error($"SettingsWindow: mode change failed: {ex}"); }
                finally { controller?.CloseStatusPill(); }
            }
            else if (_settings.Mode == TranscriptionMode.Server && (modelChanged || serverLaunchChanged) && app is not null)
            {
                // Same mode but the server was reconfigured: restart it to pick up new launch
                // settings (executable/host/port/arguments) or the new model.
                try { await app.RestartServerAsync(); }
                catch (Exception ex) { Logger.Error($"SettingsWindow: server restart failed: {ex}"); }
            }

            // Hotkey change: re-register the global hotkey immediately.
            if (hotkeyChanged || toggleHotkeyChanged)
                app?.ReapplyHotkey();

            // Mic change: the controller recreates its AudioCaptureService for the newly selected
            // device on the next session (see EnsureAudioCaptureInitialized). Switching the live
            // NAudio capture mid-session is avoided because the active writer and stream consumers
            // are bound to the current instance; if a session is in progress, tell the user the new
            // mic applies from the next session.
            var micChanged = previousMic != _settings.MicrophoneDeviceIndex;
            if (micChanged && controller is not null && controller.State != DictationState.Idle)
                app?.ShowNote("Microphone change will apply on the next dictation session.");

            controller?.RefreshModelName();

            // Reflect the (possibly changed) tray-toggle setting on the tray context menu.
            app?.SyncTrayToggleMode(_settings.UseTrayIconToggle);

            Close();
        }

        /// <summary>
        /// Parses a required positive (&gt; 0) integer, showing a validation message and returning
        /// false when the value is missing or invalid.
        /// </summary>
        private bool TryParsePositiveInt(string? text, string fieldName, out int value)
        {
            if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
                return true;

            MessageBox.Show(this, $"{fieldName} must be a positive whole number.", "VoiceType",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        /// <summary>
        /// Parses a required TCP port (1-65535), showing a validation message on failure.
        /// </summary>
        private bool TryParsePort(string? text, out int value)
        {
            if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                value is >= 1 and <= 65535)
                return true;

            MessageBox.Show(this, "Server port must be between 1 and 65535.", "VoiceType",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

        private void ServerExecutableBrowseButton_Click(object sender, RoutedEventArgs e)
            => BrowseForExecutable(ServerExecutableTextBox);

        private void StreamExecutableBrowseButton_Click(object sender, RoutedEventArgs e)
            => BrowseForExecutable(StreamExecutableTextBox);

        private void CliExecutableBrowseButton_Click(object sender, RoutedEventArgs e)
            => BrowseForExecutable(CliExecutableTextBox);

        /// <summary>
        /// Opens a file picker for an .exe and writes the chosen path into <paramref name="target"/>.
        /// </summary>
        private void BrowseForExecutable(TextBox target)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select executable",
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };

            var current = target.Text;
            if (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    var dir = Path.GetDirectoryName(Path.GetFullPath(current));
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        dialog.InitialDirectory = dir;
                }
                catch { }
            }

            if (dialog.ShowDialog(this) == true)
                target.Text = dialog.FileName;
        }

        /// <summary>
        /// Opens a folder picker for the temp directory and writes the chosen path into the box.
        /// </summary>
        private void TempDirectoryBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select temp directory"
            };

            var current = TempDirectoryTextBox.Text;
            if (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    var full = Path.GetFullPath(current);
                    if (Directory.Exists(full))
                        dialog.InitialDirectory = full;
                }
                catch { }
            }

            if (dialog.ShowDialog(this) == true)
                TempDirectoryTextBox.Text = dialog.FolderName;
        }

        /// <summary>
        /// Opens the settings window, or focuses the already-open one. Must be called on the
        /// UI thread.
        /// </summary>
        public static void ShowSingleInstance()
        {
            if (_instance is not null)
            {
                if (_instance.WindowState == WindowState.Minimized)
                {
                    _instance.WindowState = WindowState.Normal;
                }

                _instance.Activate();
                return;
            }

            _instance = new SettingsWindow();
            _instance.Closed += (_, _) =>
            {
                _instance = null;
                // Re-enable the dictation hotkey once Settings closes.
                SetHotkeySuspended(false);
            };

            // Silence the dictation hotkey while Settings is open so pressing the combo (e.g. to
            // capture a new one) doesn't also start a dictation session.
            SetHotkeySuspended(true);
            _instance.Show();
            _instance.Activate();
        }

        /// <summary>
        /// Suspends or resumes global hotkey activations. The keyboard hook stays installed so
        /// the press-to-capture control keeps working; only dictation triggering is affected.
        /// </summary>
        private static void SetHotkeySuspended(bool suspended)
        {
            if (Application.Current?.Resources["HotkeyManager"] is Infrastructure.Hotkeys.GlobalHotkeyManager hotkeyManager)
                hotkeyManager.IsSuspended = suspended;
        }
    }
}
