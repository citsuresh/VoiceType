using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Settings for spoken-punctuation replacement: a global toggle plus a per-rule editable list
    /// mapping dictated phrases (e.g. "comma", "new paragraph") to inserted punctuation. Each rule
    /// can be enabled/disabled individually without deleting it.
    /// </summary>
    public partial class SpokenPunctuationSection : UserControl, ISettingsSection
    {
        private readonly ObservableCollection<SpokenPunctuationRule> _rules = new();

        public SpokenPunctuationSection()
        {
            InitializeComponent();
            RulesGrid.ItemsSource = _rules;
        }

        public string Title => "Spoken punctuation";

        public string SearchKeywords =>
            "post-processing spoken punctuation comma period full stop question exclamation mark colon semicolon quote parenthesis new line paragraph hyphen dash";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            EnableSpokenPunctuationCheckBox.IsChecked = settings.EnableSpokenPunctuation;

            _rules.Clear();
            if (settings.SpokenPunctuationRules is not null)
            {
                foreach (var rule in settings.SpokenPunctuationRules)
                    _rules.Add(new SpokenPunctuationRule(rule.Phrase, rule.Replacement, rule.IsEnabled));
            }

            UpdateEnabledState();
        }

        public bool Validate()
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in _rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Phrase))
                {
                    MessageBox.Show(Window.GetWindow(this), "Spoken-punctuation phrases cannot be blank.", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (string.IsNullOrEmpty(rule.Replacement))
                {
                    MessageBox.Show(Window.GetWindow(this), $"Rule \"{rule.Phrase.Trim()}\" has no replacement.", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (!seen.Add(rule.Phrase.Trim()))
                {
                    MessageBox.Show(Window.GetWindow(this), $"Duplicate phrase: \"{rule.Phrase.Trim()}\".", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            settings.EnableSpokenPunctuation = EnableSpokenPunctuationCheckBox.IsChecked == true;
            settings.SpokenPunctuationRules = _rules
                .Select(r => new SpokenPunctuationRule(r.Phrase.Trim(), r.Replacement, r.IsEnabled))
                .ToList();
        }

        private void EnableSpokenPunctuationCheckBox_Changed(object sender, RoutedEventArgs e)
            => UpdateEnabledState();

        private void UpdateEnabledState()
        {
            var enabled = EnableSpokenPunctuationCheckBox.IsChecked == true;
            RulesGrid.IsEnabled = enabled;
            AddRuleButton.IsEnabled = enabled;
            EditRuleButton.IsEnabled = enabled;
            RemoveRuleButton.IsEnabled = enabled;
            RestoreRulesButton.IsEnabled = enabled;
            RefreshEnableAllState();
        }

        // Reflects the aggregate enabled state of all rules as the header tri-state checkbox.
        private void RefreshEnableAllState()
        {
            if (_rules.Count == 0)
            {
                EnableAllCheckBox.IsChecked = false;
                return;
            }

            var enabledCount = _rules.Count(r => r.IsEnabled);
            EnableAllCheckBox.IsChecked = enabledCount == 0 ? false
                : enabledCount == _rules.Count ? true
                : (bool?)null;
        }

        private void EnableAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            // Toggle all rules on unless every rule is already enabled, in which case turn all off.
            var enableAll = _rules.Any(r => !r.IsEnabled);
            foreach (var rule in _rules)
                rule.IsEnabled = enableAll;

            RulesGrid.Items.Refresh();
            RefreshEnableAllState();
        }

        private void AddRuleButton_Click(object sender, RoutedEventArgs e)
        {
            var rule = PromptForRule("Add spoken-punctuation rule", null);
            if (rule is null) return;

            if (_rules.Any(r => string.Equals(r.Phrase.Trim(), rule.Phrase.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(Window.GetWindow(this), $"\"{rule.Phrase.Trim()}\" is already in the list.", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _rules.Add(rule);
            RefreshEnableAllState();
        }

        private void EditRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not SpokenPunctuationRule selected) return;

            var index = _rules.IndexOf(selected);
            var edited = PromptForRule("Edit spoken-punctuation rule", selected);
            if (edited is null) return;

            if (_rules.Where((_, i) => i != index)
                .Any(r => string.Equals(r.Phrase.Trim(), edited.Phrase.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(Window.GetWindow(this), $"\"{edited.Phrase.Trim()}\" is already in the list.", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _rules[index] = edited;
            RefreshEnableAllState();
        }

        private void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not SpokenPunctuationRule selected) return;
            _rules.Remove(selected);
            RefreshEnableAllState();
        }

        // Re-adds or re-enables the built-in recommended rules without touching user-added rules.
        private void RestoreRulesButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var recommended in VoiceTypeSettings.DefaultSpokenPunctuationRules())
            {
                var existing = _rules.FirstOrDefault(
                    r => string.Equals(r.Phrase.Trim(), recommended.Phrase, StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                    _rules.Add(new SpokenPunctuationRule(recommended.Phrase, recommended.Replacement, recommended.IsEnabled));
                else
                    existing.IsEnabled = existing.IsEnabled || recommended.IsEnabled;
            }

            RulesGrid.Items.Refresh();
            RefreshEnableAllState();
        }

        // Small modal editor for a rule's phrase and replacement. Returns null on cancel. The two
        // whitespace outputs are offered as readable tokens so users never type invisible characters.
        private SpokenPunctuationRule? PromptForRule(string title, SpokenPunctuationRule? initial)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 340,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(12) };

            panel.Children.Add(new TextBlock { Text = "Spoken phrase", Margin = new Thickness(0, 0, 0, 2) });
            var phraseBox = new TextBox { Text = initial?.Phrase ?? string.Empty, Margin = new Thickness(0, 0, 0, 10) };

            panel.Children.Add(phraseBox);

            panel.Children.Add(new TextBlock { Text = "Inserts", Margin = new Thickness(0, 0, 0, 2) });
            var replacementBox = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 0, 10) };
            replacementBox.Items.Add(VoiceTypeSettings.LineBreakToken);
            replacementBox.Items.Add(VoiceTypeSettings.ParagraphBreakToken);
            replacementBox.Text = initial?.Replacement ?? string.Empty;
            panel.Children.Add(replacementBox);

            var enabledCheck = new CheckBox { Content = "Enabled", IsChecked = initial?.IsEnabled ?? true, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(enabledCheck);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };
            okButton.Click += (_, _) => { dialog.DialogResult = true; };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            panel.Children.Add(buttons);

            dialog.Content = panel;
            phraseBox.Focus();

            if (dialog.ShowDialog() != true) return null;

            var phrase = phraseBox.Text.Trim();
            var replacement = replacementBox.Text;
            if (phrase.Length == 0 || string.IsNullOrEmpty(replacement)) return null;

            return new SpokenPunctuationRule(phrase, replacement, enabledCheck.IsChecked == true);
        }
    }
}
