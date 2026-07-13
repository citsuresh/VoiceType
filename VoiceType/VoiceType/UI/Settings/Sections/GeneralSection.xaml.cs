using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using Microsoft.Win32;
using NAudio.Wave;
using VoiceType.Infrastructure.Config;
using VoiceType.Infrastructure.Logging;
using VoiceType.Infrastructure.Whisper;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// General settings: whisper model, capture microphone, language and temp directory.
    /// </summary>
    public partial class GeneralSection : UserControl, ISettingsSection
    {
        private sealed record ModelItem(string Path, string DisplayName);
        private sealed record MicItem(int Index, string Name);

        public GeneralSection()
        {
            InitializeComponent();
        }

        public string Title => "General";

        public string SearchKeywords => "model whisper microphone mic audio device language locale temp directory folder path";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            // Models.
            var models = new List<ModelItem>();
            try
            {
                foreach (var path in new WhisperProcessRunner(settings).EnumerateModels())
                    models.Add(new ModelItem(path, Path.GetFileNameWithoutExtension(path)));
            }
            catch (Exception ex)
            {
                Logger.Error($"GeneralSection: failed to enumerate models: {ex.Message}");
            }

            // Ensure the currently configured model is present even if it lives outside the folder.
            if (!string.IsNullOrWhiteSpace(settings.WhisperModelPath) &&
                !models.Exists(m => string.Equals(Path.GetFileName(m.Path), Path.GetFileName(settings.WhisperModelPath), StringComparison.OrdinalIgnoreCase)))
            {
                models.Insert(0, new ModelItem(settings.WhisperModelPath, Path.GetFileNameWithoutExtension(settings.WhisperModelPath)));
            }

            ModelComboBox.ItemsSource = models;
            var activeName = string.IsNullOrEmpty(settings.WhisperModelPath) ? null : Path.GetFileName(settings.WhisperModelPath);
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
                Logger.Error($"GeneralSection: failed to enumerate microphones: {ex.Message}");
            }

            if (mics.Count == 0)
                mics.Add(new MicItem(0, "Default microphone"));

            MicrophoneComboBox.ItemsSource = mics;
            MicrophoneComboBox.SelectedItem = mics.Find(m => m.Index == settings.MicrophoneDeviceIndex) ?? mics[0];

            LanguageTextBox.Text = settings.Language ?? string.Empty;
            TempDirectoryTextBox.Text = settings.TempDirectory ?? string.Empty;
        }

        public bool Validate() => true;

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (ModelComboBox.SelectedItem is ModelItem model)
                settings.WhisperModelPath = model.Path;
            if (MicrophoneComboBox.SelectedItem is MicItem mic)
                settings.MicrophoneDeviceIndex = mic.Index;

            settings.Language = LanguageTextBox.Text?.Trim() ?? string.Empty;
            settings.TempDirectory = TempDirectoryTextBox.Text?.Trim() ?? string.Empty;
        }

        private void TempDirectoryBrowseButton_Click(object sender, System.Windows.RoutedEventArgs e)
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

            if (dialog.ShowDialog(System.Windows.Window.GetWindow(this)) == true)
                TempDirectoryTextBox.Text = dialog.FolderName;
        }
    }
}
