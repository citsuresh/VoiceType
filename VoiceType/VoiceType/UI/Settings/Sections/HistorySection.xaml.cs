using System;
using System.Windows.Controls;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Transcript-history settings: the opt-in enable toggle for persisted local history and the
    /// configurable retention limit (see <see cref="Infrastructure.History.TranscriptHistoryService"/>).
    /// </summary>
    public partial class HistorySection : UserControl, ISettingsSection
    {
        public HistorySection()
        {
            InitializeComponent();
        }

        public string Title => "Transcript history";

        public string SearchKeywords => "history transcript retention limit privacy opt-in disable enable";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            EnableHistoryCheckBox.IsChecked = settings.EnableTranscriptHistory;
            RetentionLimitTextBox.Text = settings.TranscriptHistoryRetentionLimit.ToString();
        }

        public bool Validate()
        {
            return SettingsInput.TryParsePositiveInt(this, RetentionLimitTextBox.Text, "Retention limit", out _);
        }

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            settings.EnableTranscriptHistory = EnableHistoryCheckBox.IsChecked == true;

            if (SettingsInput.TryParsePositiveInt(this, RetentionLimitTextBox.Text, "Retention limit", out var limit))
                settings.TranscriptHistoryRetentionLimit = limit;
        }
    }
}
