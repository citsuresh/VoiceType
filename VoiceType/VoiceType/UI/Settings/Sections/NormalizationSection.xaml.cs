using System;
using System.Windows.Controls;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Settings for optional transcript whitespace, capitalization, and punctuation normalization.
    /// </summary>
    public partial class NormalizationSection : UserControl, ISettingsSection
    {
        public NormalizationSection()
        {
            InitializeComponent();
        }

        public string Title => "Normalization";

        public string SearchKeywords =>
            "post-processing normalize normalization punctuation capitalize capitalization whitespace trim collapse spaces sentence";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            TrimWhitespaceCheckBox.IsChecked = settings.PostProcessTrimWhitespace;
            CollapseSpacesCheckBox.IsChecked = settings.PostProcessCollapseSpaces;
            CapitalizeSentencesCheckBox.IsChecked = settings.PostProcessCapitalizeSentences;
        }

        public bool Validate() => true;

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            settings.PostProcessTrimWhitespace = TrimWhitespaceCheckBox.IsChecked == true;
            settings.PostProcessCollapseSpaces = CollapseSpacesCheckBox.IsChecked == true;
            settings.PostProcessCapitalizeSentences = CapitalizeSentencesCheckBox.IsChecked == true;
        }
    }
}
