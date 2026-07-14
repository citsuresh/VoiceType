using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Post-processing settings: the toggleable transcript normalization steps (trim, collapse
    /// spaces, capitalize sentences, add trailing period) and the editable filler-word list.
    /// These feed the pipeline in <see cref="Core.DictationSessionController"/>.
    /// </summary>
    public partial class PostProcessingSection : UserControl, ISettingsSection
    {
        // Backing collection for the filler-word ListBox so Add/Edit/Remove update the UI live.
        private readonly ObservableCollection<string> _fillerWords = new();

        public PostProcessingSection()
        {
            InitializeComponent();
            FillerWordsListBox.ItemsSource = _fillerWords;
        }

        public string Title => "Post-processing";

        public string SearchKeywords =>
            "post-processing punctuation capitalize capitalization filler whitespace trim collapse spaces cleanup normalize period sentence";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            TrimWhitespaceCheckBox.IsChecked = settings.PostProcessTrimWhitespace;
            CollapseSpacesCheckBox.IsChecked = settings.PostProcessCollapseSpaces;
            CapitalizeSentencesCheckBox.IsChecked = settings.PostProcessCapitalizeSentences;
            AddTrailingPeriodCheckBox.IsChecked = settings.PostProcessAddTrailingPeriod;

            RemoveFillerWordsCheckBox.IsChecked = settings.RemoveFillerWords;

            _fillerWords.Clear();
            if (settings.FillerWords is not null)
            {
                foreach (var word in settings.FillerWords)
                    _fillerWords.Add(word);
            }

            UpdateFillerListEnabled();
        }

        public bool Validate()
        {
            // Reject blank or duplicate (case-insensitive) filler entries.
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in _fillerWords)
            {
                if (string.IsNullOrWhiteSpace(word))
                {
                    MessageBox.Show(Window.GetWindow(this), "Filler words cannot be blank.", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (!seen.Add(word.Trim()))
                {
                    MessageBox.Show(Window.GetWindow(this), $"Duplicate filler word: \"{word.Trim()}\".", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            settings.PostProcessTrimWhitespace = TrimWhitespaceCheckBox.IsChecked == true;
            settings.PostProcessCollapseSpaces = CollapseSpacesCheckBox.IsChecked == true;
            settings.PostProcessCapitalizeSentences = CapitalizeSentencesCheckBox.IsChecked == true;
            settings.PostProcessAddTrailingPeriod = AddTrailingPeriodCheckBox.IsChecked == true;

            settings.RemoveFillerWords = RemoveFillerWordsCheckBox.IsChecked == true;
            settings.FillerWords = _fillerWords.Select(w => w.Trim()).ToList();
        }

        private void RemoveFillerWordsCheckBox_Changed(object sender, RoutedEventArgs e)
            => UpdateFillerListEnabled();

        private void UpdateFillerListEnabled()
        {
            var enabled = RemoveFillerWordsCheckBox.IsChecked == true;
            FillerWordsListBox.IsEnabled = enabled;
            AddFillerButton.IsEnabled = enabled;
            EditFillerButton.IsEnabled = enabled;
            RemoveFillerButton.IsEnabled = enabled;
        }

        private void AddFillerButton_Click(object sender, RoutedEventArgs e)
        {
            var value = PromptForWord("Add filler word", string.Empty);
            if (value is null) return;

            value = value.Trim();
            if (value.Length == 0) return;

            if (_fillerWords.Any(w => string.Equals(w, value, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(Window.GetWindow(this), $"\"{value}\" is already in the list.", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _fillerWords.Add(value);
        }

        private void EditFillerButton_Click(object sender, RoutedEventArgs e)
        {
            if (FillerWordsListBox.SelectedIndex < 0) return;

            var index = FillerWordsListBox.SelectedIndex;
            var value = PromptForWord("Edit filler word", _fillerWords[index]);
            if (value is null) return;

            value = value.Trim();
            if (value.Length == 0) return;

            if (_fillerWords.Where((_, i) => i != index)
                .Any(w => string.Equals(w, value, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(Window.GetWindow(this), $"\"{value}\" is already in the list.", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _fillerWords[index] = value;
        }

        private void RemoveFillerButton_Click(object sender, RoutedEventArgs e)
        {
            if (FillerWordsListBox.SelectedIndex < 0) return;
            _fillerWords.RemoveAt(FillerWordsListBox.SelectedIndex);
        }

        // Shows a small modal prompt for a single filler word/phrase. Returns null on cancel.
        private string? PromptForWord(string title, string initial)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 320,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(12) };
            var textBox = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 12) };
            textBox.SelectAll();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };

            okButton.Click += (_, _) => { dialog.DialogResult = true; };

            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            panel.Children.Add(textBox);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            textBox.Focus();

            return dialog.ShowDialog() == true ? textBox.Text : null;
        }
    }
}
