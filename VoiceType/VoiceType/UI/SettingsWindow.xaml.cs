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

        // Root nodes of the navigation tree; leaves wrap a section from _sections.
        private List<NavNode> _rootNodes = new();

        public SettingsWindow()
        {
            InitializeComponent();

            _settings = Application.Current?.Resources["Settings"] as VoiceTypeSettings
                        ?? SettingsLoader.Load();

            var generalSection = new GeneralSection();
            var transcriptionSection = new TranscriptionSection();
            var dictationSection = new DictationSection();
            var textInsertionSection = new TextInsertionSection();
            var normalizationSection = new NormalizationSection();
            var fillerWordsSection = new FillerWordsSection();

            _sections = new List<UserControl>
            {
                generalSection,
                transcriptionSection,
                dictationSection,
                textInsertionSection,
                normalizationSection,
                fillerWordsSection,
            };

            foreach (var section in _sections)
                ((ISettingsSection)section).Load(_settings);

            _rootNodes = new List<NavNode>
            {
                new(((ISettingsSection)generalSection).Title, generalSection),
                new(((ISettingsSection)transcriptionSection).Title, transcriptionSection),
                new(((ISettingsSection)dictationSection).Title, dictationSection),
                new(((ISettingsSection)textInsertionSection).Title, textInsertionSection),
                new("Post-processing")
                {
                    Children =
                    {
                        new(((ISettingsSection)normalizationSection).Title, normalizationSection),
                        new(((ISettingsSection)fillerWordsSection).Title, fillerWordsSection),
                    }
                }
            };

            RefreshNavTree(string.Empty);
        }

        /// <summary>
        /// Rebuilds the navigation tree, filtering by <paramref name="filter"/> against each
        /// selectable node's title and search keywords. A parent node is kept (with only its
        /// matching children) when any descendant matches. The currently selected section is
        /// preserved when it still matches.
        /// </summary>
        private void RefreshNavTree(string filter)
        {
            var previousSection = _rootNodes.SelectMany(FlattenNodes).FirstOrDefault(n => n.IsSelected)?.Section;

            List<NavNode> matches;
            if (string.IsNullOrWhiteSpace(filter))
            {
                matches = _rootNodes;
            }
            else
            {
                var term = filter.Trim();
                matches = _rootNodes
                    .Select(n => FilterNode(n, term))
                    .Where(n => n is not null)
                    .Select(n => n!)
                    .ToList();
            }

            NavTree.ItemsSource = matches;

            var flattened = matches.SelectMany(FlattenNodes).Where(n => n.Section is not null).ToList();
            var restored = previousSection is not null
                ? flattened.FirstOrDefault(n => ReferenceEquals(n.Section, previousSection))
                : null;

            if (restored is not null)
                restored.IsSelected = true;
            else if (flattened.Count > 0)
                flattened[0].IsSelected = true;
        }

        /// <summary>
        /// Returns a filtered copy of <paramref name="node"/> (with only matching descendants)
        /// when it or any descendant matches <paramref name="term"/>; otherwise null.
        /// </summary>
        private static NavNode? FilterNode(NavNode node, string term)
        {
            var section = node.Section as ISettingsSection;
            var selfMatch = section is not null &&
                (section.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                 section.SearchKeywords.Contains(term, StringComparison.OrdinalIgnoreCase));

            var filteredChildren = node.Children
                .Select(c => FilterNode(c, term))
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();

            if (!selfMatch && filteredChildren.Count == 0)
                return null;

            var result = new NavNode(node.Title, node.Section) { IsExpanded = true };
            result.Children.AddRange(selfMatch ? node.Children : filteredChildren);
            return result;
        }

        private static IEnumerable<NavNode> FlattenNodes(NavNode node)
        {
            yield return node;
            foreach (var child in node.Children)
                foreach (var descendant in FlattenNodes(child))
                    yield return descendant;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
            => RefreshNavTree(SearchTextBox.Text);

        private void NavTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // A parent category node (no Section) is non-selectable: it only groups children, so
            // ignore its selection and leave the previously displayed section in place.
            if (e.NewValue is not NavNode { Section: not null } node)
                return;

            SectionHost.Content = node.Section;
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
        /// filter first so the section is guaranteed to be present in the tree.
        /// </summary>
        private void SelectSection(UserControl section)
        {
            if (!string.IsNullOrEmpty(SearchTextBox.Text))
                SearchTextBox.Text = string.Empty;

            var node = _rootNodes.SelectMany(FlattenNodes).FirstOrDefault(n => ReferenceEquals(n.Section, section));
            if (node is not null)
                node.IsSelected = true;
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

            }
        }
