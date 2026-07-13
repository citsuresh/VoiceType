using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Dictation settings: hold-to-talk and toggle mode, including press-to-capture hotkey boxes,
    /// enable toggles, tray-icon toggle, and idle auto-stop.
    /// </summary>
    public partial class DictationSection : UserControl, ISettingsSection
    {
        // Pending hold-to-talk hotkey, normalised to the string form GlobalHotkeyManager expects
        // (modifiers joined by '+', key = a System.Windows.Input.Key name).
        private string _capturedModifiers = string.Empty;
        private string _capturedKey = string.Empty;
        private string _preCaptureModifiers = string.Empty;
        private string _preCaptureKey = string.Empty;

        // Pending toggle-mode hotkey in the same normalised string form.
        private string _capturedToggleModifiers = string.Empty;
        private string _capturedToggleKey = string.Empty;
        private string _preCaptureToggleModifiers = string.Empty;
        private string _preCaptureToggleKey = string.Empty;

        public DictationSection()
        {
            InitializeComponent();
        }

        public string Title => "Dictation";

        public string SearchKeywords => "hold to talk push toggle mode hotkey shortcut key tray icon idle auto stop timeout";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            _capturedModifiers = settings.HotkeyModifiers ?? string.Empty;
            _capturedKey = settings.HotkeyKey ?? string.Empty;
            UpdateHotkeyDisplay();

            DictationHotkeyEnabledCheckBox.IsChecked = settings.DictationHotkeyEnabled;
            UpdateHoldToTalkFieldsEnabled();

            _capturedToggleModifiers = settings.ToggleHotkeyModifiers ?? string.Empty;
            _capturedToggleKey = settings.ToggleHotkeyKey ?? string.Empty;
            UpdateToggleHotkeyDisplay();

            ToggleModeEnabledCheckBox.IsChecked = settings.ToggleModeEnabled;
            UpdateToggleModeFieldsEnabled();

            UseTrayIconToggleCheckBox.IsChecked = settings.UseTrayIconToggle;
            ToggleIdleAutoStopCheckBox.IsChecked = settings.ToggleIdleAutoStopEnabled;
            ToggleIdleAutoStopSecondsTextBox.Text = settings.ToggleIdleAutoStopSeconds.ToString(CultureInfo.InvariantCulture);
        }

        public bool Validate()
        {
            if (!SettingsInput.TryParsePositiveInt(this, ToggleIdleAutoStopSecondsTextBox.Text, "Idle timeout (seconds)", out _))
                return false;

            if (!ValidateCapturedHotkey())
                return false;

            if (!ValidateCapturedToggleHotkey())
                return false;

            return true;
        }

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            settings.DictationHotkey = VoiceTypeSettings.CombineHotkey(_capturedModifiers, _capturedKey);
            settings.ToggleHotkey = VoiceTypeSettings.CombineHotkey(_capturedToggleModifiers, _capturedToggleKey);
            settings.DictationHotkeyEnabled = DictationHotkeyEnabledCheckBox.IsChecked == true;
            settings.ToggleModeEnabled = ToggleModeEnabledCheckBox.IsChecked == true;

            settings.UseTrayIconToggle = UseTrayIconToggleCheckBox.IsChecked == true;
            settings.ToggleIdleAutoStopEnabled = ToggleIdleAutoStopCheckBox.IsChecked == true;
            if (int.TryParse(ToggleIdleAutoStopSecondsTextBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idleSeconds))
                settings.ToggleIdleAutoStopSeconds = idleSeconds;
        }

        // ---- Enable toggles ----

        private void DictationHotkeyEnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
            => UpdateHoldToTalkFieldsEnabled();

        private void ToggleModeEnabledCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
            => UpdateToggleModeFieldsEnabled();

        private void UpdateHoldToTalkFieldsEnabled()
            => HoldToTalkFieldsGrid.IsEnabled = DictationHotkeyEnabledCheckBox.IsChecked == true;

        private void UpdateToggleModeFieldsEnabled()
            => ToggleModeFieldsPanel.IsEnabled = ToggleModeEnabledCheckBox.IsChecked == true;

        // ---- Hold-to-talk hotkey capture ----

        /// <summary>
        /// Renders the currently captured hotkey combo into the read-only capture box.
        /// </summary>
        private void UpdateHotkeyDisplay()
        {
            HotkeyCaptureTextBox.Text = FormatCombo(_capturedModifiers, _capturedKey);
        }

        private void HotkeyCaptureTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _preCaptureModifiers = _capturedModifiers;
            _preCaptureKey = _capturedKey;
            ClearHotkeyValidation();
            HotkeyCaptureTextBox.Text = "Press a key combination...";
        }

        private void HotkeyCaptureTextBox_LostFocus(object sender, RoutedEventArgs e)
            => UpdateHotkeyDisplay();

        private void HotkeyCaptureTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key is Key.Tab)
                return;

            e.Handled = true;

            if (key == Key.Escape)
            {
                _capturedModifiers = _preCaptureModifiers;
                _capturedKey = _preCaptureKey;
                ClearHotkeyValidation();
                UpdateHotkeyDisplay();
                Keyboard.ClearFocus();
                return;
            }

            if (TryCaptureCombo(key, out var modifiers, out var mainKey, out var runningDisplay))
            {
                _capturedModifiers = modifiers;
                _capturedKey = mainKey;
                ClearHotkeyValidation();
                HotkeyCaptureTextBox.Text = FormatCombo(modifiers, mainKey);
            }
            else
            {
                HotkeyCaptureTextBox.Text = runningDisplay;
            }
        }

        /// <summary>
        /// Validates the captured hold-to-talk combo. A valid combo requires a non-modifier main
        /// key (a lone modifier is rejected) whose name parses to a <see cref="Key"/>.
        /// </summary>
        private bool ValidateCapturedHotkey()
        {
            var error = ValidateCombo(_capturedModifiers, _capturedKey);
            if (error is null)
            {
                ClearHotkeyValidation();
                return true;
            }

            ShowHotkeyValidation(error);
            return false;
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

        // ---- Toggle-mode hotkey capture ----

        private void UpdateToggleHotkeyDisplay()
        {
            ToggleHotkeyCaptureTextBox.Text = FormatCombo(_capturedToggleModifiers, _capturedToggleKey);
        }

        private void ToggleHotkeyCaptureTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _preCaptureToggleModifiers = _capturedToggleModifiers;
            _preCaptureToggleKey = _capturedToggleKey;
            ClearToggleHotkeyValidation();
            ToggleHotkeyCaptureTextBox.Text = "Press a key combination...";
        }

        private void ToggleHotkeyCaptureTextBox_LostFocus(object sender, RoutedEventArgs e)
            => UpdateToggleHotkeyDisplay();

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

            if (TryCaptureCombo(key, out var modifiers, out var mainKey, out var runningDisplay))
            {
                _capturedToggleModifiers = modifiers;
                _capturedToggleKey = mainKey;
                ClearToggleHotkeyValidation();
                ToggleHotkeyCaptureTextBox.Text = FormatCombo(modifiers, mainKey);
            }
            else
            {
                ToggleHotkeyCaptureTextBox.Text = runningDisplay;
            }
        }

        private bool ValidateCapturedToggleHotkey()
        {
            var error = ValidateCombo(_capturedToggleModifiers, _capturedToggleKey);
            if (error is null)
            {
                ClearToggleHotkeyValidation();
                return true;
            }

            ShowToggleHotkeyValidation(error);
            return false;
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

        // ---- Shared capture helpers ----

        /// <summary>
        /// Formats a normalised modifier/key pair for display in a capture box, or "(none)".
        /// </summary>
        private static string FormatCombo(string modifiers, string key)
        {
            string combo;
            if (string.IsNullOrWhiteSpace(modifiers))
            {
                combo = key;
            }
            else
            {
                var mods = string.Join(" + ", modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                combo = string.IsNullOrWhiteSpace(key) ? mods : $"{mods} + {key}";
            }

            return string.IsNullOrWhiteSpace(combo) ? "(none)" : combo;
        }

        /// <summary>
        /// Captures the pressed key combination and normalises it into modifier/key strings that
        /// <see cref="Infrastructure.Hotkeys.GlobalHotkeyManager"/> accepts. Returns false while
        /// only modifier keys are held (combo still in progress), providing a running-display string.
        /// </summary>
        private static bool TryCaptureCombo(Key key, out string modifiers, out string mainKey, out string runningDisplay)
        {
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
                modifiers = string.Empty;
                mainKey = string.Empty;
                runningDisplay = string.Join(" + ", running) + " + ...";
                return false;
            }

            modifiers = string.Join("+", mods);
            mainKey = key.ToString();
            runningDisplay = string.Empty;
            return true;
        }

        /// <summary>
        /// Returns an error message when the captured combo is not a valid hotkey, or null when valid.
        /// </summary>
        private static string? ValidateCombo(string modifiers, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "Press a main key (e.g. a letter or F-key) in addition to any modifiers.";

            if (key is "LeftCtrl" or "RightCtrl" or "LeftAlt" or "RightAlt"
                or "LeftShift" or "RightShift" or "LWin" or "RWin")
            {
                if (string.IsNullOrWhiteSpace(modifiers))
                    return "A modifier alone is not a valid hotkey; add a main key.";
            }

            if (!Enum.TryParse<Key>(key, out _))
                return "The captured key is not recognised; try a different combination.";

            return null;
        }
    }
}
