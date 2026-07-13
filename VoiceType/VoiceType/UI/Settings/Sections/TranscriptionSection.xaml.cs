using System;
using System.Globalization;
using System.Windows.Controls;
using VoiceType.Core;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Transcription settings: mode selection, preview timings, and the per-mode launch settings
    /// for Server, Stream and CLI. Only the panel matching the selected mode is shown.
    /// </summary>
    public partial class TranscriptionSection : UserControl, ISettingsSection
    {
        public TranscriptionSection()
        {
            InitializeComponent();
        }

        public string Title => "Transcription";

        public string SearchKeywords => "mode preview chunk throttle server stream cli executable host port timeout arguments";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            ModeComboBox.ItemsSource = Enum.GetValues(typeof(TranscriptionMode));
            ModeComboBox.SelectedItem = settings.Mode;

            PreviewChunkTextBox.Text = settings.PreviewChunkMilliseconds.ToString(CultureInfo.InvariantCulture);
            PreviewThrottleTextBox.Text = settings.PreviewThrottleMilliseconds.ToString(CultureInfo.InvariantCulture);

            // Server fields.
            ServerExecutableTextBox.Text = settings.WhisperServerExecutablePath ?? string.Empty;
            ServerHostTextBox.Text = settings.WhisperServerHost ?? string.Empty;
            ServerPortTextBox.Text = settings.WhisperServerPort.ToString(CultureInfo.InvariantCulture);
            ServerTimeoutTextBox.Text = settings.WhisperServerTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            ServerArgumentsTextBox.Text = settings.WhisperServerArguments ?? string.Empty;

            // Stream fields.
            StreamExecutableTextBox.Text = settings.WhisperStreamExecutablePath ?? string.Empty;
            StreamArgumentsTextBox.Text = settings.WhisperStreamArguments ?? string.Empty;

            // CLI fields.
            CliExecutableTextBox.Text = settings.WhisperCliExecutablePath ?? string.Empty;

            UpdateModeSectionsVisibility(settings.Mode);
        }

        public bool Validate()
        {
            return SettingsInput.TryParsePositiveInt(this, PreviewChunkTextBox.Text, "Preview chunk (ms)", out _) &&
                   SettingsInput.TryParsePositiveInt(this, PreviewThrottleTextBox.Text, "Preview throttle (ms)", out _) &&
                   SettingsInput.TryParsePositiveInt(this, ServerTimeoutTextBox.Text, "Server timeout (s)", out _) &&
                   SettingsInput.TryParsePort(this, ServerPortTextBox.Text, out _);
        }

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (ModeComboBox.SelectedItem is TranscriptionMode mode)
                settings.Mode = mode;

            // Validate() ran first, so these parse cleanly.
            if (int.TryParse(PreviewChunkTextBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var previewChunk))
                settings.PreviewChunkMilliseconds = previewChunk;
            if (int.TryParse(PreviewThrottleTextBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var previewThrottle))
                settings.PreviewThrottleMilliseconds = previewThrottle;

            settings.WhisperServerExecutablePath = ServerExecutableTextBox.Text?.Trim() ?? string.Empty;
            settings.WhisperServerHost = ServerHostTextBox.Text?.Trim() ?? string.Empty;
            if (int.TryParse(ServerPortTextBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var serverPort))
                settings.WhisperServerPort = serverPort;
            if (int.TryParse(ServerTimeoutTextBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var serverTimeout))
                settings.WhisperServerTimeoutSeconds = serverTimeout;
            settings.WhisperServerArguments = ServerArgumentsTextBox.Text?.Trim() ?? string.Empty;

            settings.WhisperStreamExecutablePath = StreamExecutableTextBox.Text?.Trim() ?? string.Empty;
            settings.WhisperStreamArguments = StreamArgumentsTextBox.Text?.Trim() ?? string.Empty;

            settings.WhisperCliExecutablePath = CliExecutableTextBox.Text?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Shows only the mode-specific section (Server/Stream/CLI) that matches the selected
        /// transcription mode. WavFile has no dedicated executable settings of its own.
        /// </summary>
        private void UpdateModeSectionsVisibility(TranscriptionMode mode)
        {
            ServerGroupBox.Visibility = mode == TranscriptionMode.Server ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            StreamGroupBox.Visibility = mode == TranscriptionMode.Stream ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            CliGroupBox.Visibility = mode == TranscriptionMode.Cli ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModeComboBox.SelectedItem is TranscriptionMode mode)
                UpdateModeSectionsVisibility(mode);
        }

        private void ServerExecutableBrowseButton_Click(object sender, System.Windows.RoutedEventArgs e)
            => SettingsInput.BrowseForExecutable(ServerExecutableTextBox);

        private void StreamExecutableBrowseButton_Click(object sender, System.Windows.RoutedEventArgs e)
            => SettingsInput.BrowseForExecutable(StreamExecutableTextBox);

        private void CliExecutableBrowseButton_Click(object sender, System.Windows.RoutedEventArgs e)
            => SettingsInput.BrowseForExecutable(CliExecutableTextBox);
    }
}
