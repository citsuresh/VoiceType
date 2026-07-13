using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings
{
    /// <summary>
    /// A single, self-contained page of the settings window. Each section is a
    /// <see cref="System.Windows.Controls.UserControl"/> that loads its values from the shared
    /// <see cref="VoiceTypeSettings"/>, validates its own input, and writes changes back. The host
    /// <see cref="SettingsWindow"/> owns navigation and the single global Save/Cancel, calling
    /// <see cref="Validate"/> then <see cref="Save"/> across every section when the user saves.
    /// </summary>
    public interface ISettingsSection
    {
        /// <summary>Display title shown in the navigation tree.</summary>
        string Title { get; }

        /// <summary>
        /// Space-separated keywords describing the fields this section contains, used so search
        /// can match on field names (e.g. "microphone", "hotkey", "clipboard") and not just the title.
        /// </summary>
        string SearchKeywords { get; }

        /// <summary>Populates the controls from the shared settings.</summary>
        void Load(VoiceTypeSettings settings);

        /// <summary>
        /// Validates the section's current input. Returns false (and surfaces a message to the
        /// user) when a value is invalid, which cancels the global Save.
        /// </summary>
        bool Validate();

        /// <summary>Writes the section's values back into the shared settings.</summary>
        void Save(VoiceTypeSettings settings);
    }
}
