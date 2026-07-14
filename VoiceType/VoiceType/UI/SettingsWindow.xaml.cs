using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VoiceType.Core;
using VoiceType.Infrastructure.Config;
using VoiceType.Infrastructure.Logging;
using VoiceType.UI.Settings;
using VoiceType.UI.Settings.Sections;

namespace VoiceType.UI
{
    /// <summary>
    /// Settings window: a searchable master-detail shell. The left navigation list selects a
    /// section (<see cref="ISettingsSection"/>) hosted on the right; Save validates and persists
    /// every section together, then applies changes live. Shown single-instance via
    /// <see cref="ShowSingleInstance"/>.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private static SettingsWindow? _instance;

        // The shared settings singleton (mutated in place so all consumers see changes).
        private readonly VoiceTypeSettings _settings;

        // Every section control, in navigation order. Each is a UserControl implementing ISettingsSection.
        private readonly List<UserControl> _sections;

        public SettingsWindow()
        {
            InitializeComponent();

            _settings = Application.Current?.Resources["Settings"] as VoiceTypeSettings
                        ?? SettingsLoader.Load();

            _sections = new List<UserControl>
            {
                new GeneralSection(),
                new TranscriptionSection(),
                new DictationSection(),
                new TextInsertionSection(),
                new PostProcessingSection(),
            };

            foreach (var section in _sections)
                ((ISettingsSection)section).Load(_settings);

            RefreshNavList(string.Empty);
            if (NavList.Items.Count > 0)
                NavList.SelectedIndex = 0;
        }

        /// <summary>
        /// Rebuilds the navigation list, filtering section titles by <paramref name="filter"/>.
        /// The currently selected section is preserved when it still matches.
        /// </summary>
        private void RefreshNavList(string filter)
        {
            var previous = NavList.SelectedItem as UserControl;

            IEnumerable<UserControl> matches = _sections;
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var term = filter.Trim();
                matches = _sections.Where(s =>
                {
                    var section = (ISettingsSection)s;
                    return section.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || section.SearchKeywords.Contains(term, StringComparison.OrdinalIgnoreCase);
                });
            }

            NavList.ItemsSource = matches
                .Select(s => new NavItem(((ISettingsSection)s).Title, s))
                .ToList();

            if (previous is not null)
            {
                var restored = NavList.Items.Cast<NavItem>().FirstOrDefault(n => ReferenceEquals(n.Section, previous));
                if (restored is not null)
                    NavList.SelectedItem = restored;
            }

            if (NavList.SelectedItem is null && NavList.Items.Count > 0)
                NavList.SelectedIndex = 0;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshNavList(SearchTextBox.Text);

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SectionHost.Content = (NavList.SelectedItem as NavItem)?.Section;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate every section before mutating settings so a bad value can't be persisted.
            // On failure, navigate to the offending section so the message is visible.
            foreach (var section in _sections)
            {
                if (!((ISettingsSection)section).Validate())
                {
                    SelectSection(section);
                    return;
                }
            }

            // Snapshot fields we compare against to decide what to apply live.
            var previousModel = _settings.WhisperModelPath;
            var previousMode = _settings.Mode;
            var previousHotkey = _settings.DictationHotkey ?? string.Empty;
            var previousToggleHotkey = _settings.ToggleHotkey ?? string.Empty;
            var previousHotkeyEnabled = _settings.DictationHotkeyEnabled;
            var previousToggleModeEnabled = _settings.ToggleModeEnabled;
            var previousMic = _settings.MicrophoneDeviceIndex;
            var previousServerExe = _settings.WhisperServerExecutablePath ?? string.Empty;
            var previousServerHost = _settings.WhisperServerHost ?? string.Empty;
            var previousServerPort = _settings.WhisperServerPort;
            var previousServerArgs = _settings.WhisperServerArguments ?? string.Empty;

            foreach (var section in _sections)
                ((ISettingsSection)section).Save(_settings);

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
            var hotkeyEnabledChanged = previousHotkeyEnabled != _settings.DictationHotkeyEnabled ||
                previousToggleModeEnabled != _settings.ToggleModeEnabled;
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
            if (hotkeyChanged || toggleHotkeyChanged || hotkeyEnabledChanged)
                app?.ReapplyHotkey();

            // Mic change: the controller recreates its AudioCaptureService for the newly selected
            // device on the next session. If a session is in progress, tell the user the new mic
            // applies from the next session.
            var micChanged = previousMic != _settings.MicrophoneDeviceIndex;
            if (micChanged && controller is not null && controller.State != DictationState.Idle)
                app?.ShowNote("Microphone change will apply on the next dictation session.");

            controller?.RefreshModelName();

            // Reflect the (possibly changed) tray-toggle setting on the tray context menu.
            app?.SyncTrayToggleMode(_settings.UseTrayIconToggle);

            Close();
        }

        /// <summary>
        /// Selects the navigation entry for <paramref name="section"/>, clearing any active search
        /// filter first so the section is guaranteed to be present in the list.
        /// </summary>
        private void SelectSection(UserControl section)
        {
            if (!string.IsNullOrEmpty(SearchTextBox.Text))
                SearchTextBox.Text = string.Empty;

            var item = NavList.Items.Cast<NavItem>().FirstOrDefault(n => ReferenceEquals(n.Section, section));
            if (item is not null)
                NavList.SelectedItem = item;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// Opens the settings window, or focuses the already-open one. Must be called on the
        /// UI thread.
        /// </summary>
        public static void ShowSingleInstance()
        {
            if (_instance is not null)
            {
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;

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

        /// <summary>Navigation list entry pairing a display title with its section control.</summary>
        private sealed record NavItem(string Title, UserControl Section);
    }
}
