using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Settings for custom removal rules: a global toggle plus a per-rule editable list of
    /// phrases to strip from the transcript, each scoped to the start, end, or anywhere within
    /// a sentence. Each rule can be enabled/disabled individually without deleting it.
    /// </summary>
    public partial class CustomRemovalRulesSection : UserControl, ISettingsSection
    {
        private readonly ObservableCollection<RemovalRuleItem> _rules = new();

        public CustomRemovalRulesSection()
        {
            InitializeComponent();
            RulesGrid.ItemsSource = _rules;
        }

        public string Title => "Custom removal rules";

        public string SearchKeywords =>
            "post-processing custom removal rules strip phrase filler sentence start end anywhere";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            EnableCustomRemovalRulesCheckBox.IsChecked = settings.EnableCustomRemovalRules;

            _rules.Clear();
            if (settings.CustomRemovalRules is not null)
            {
                foreach (var rule in settings.CustomRemovalRules)
                    _rules.Add(new RemovalRuleItem(rule.Phrase, rule.Scope, rule.IsEnabled));
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
                    MessageBox.Show(Window.GetWindow(this), "The phrase cannot be blank.", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var key = $"{rule.Phrase.Trim()}|{rule.Scope}";
                if (!seen.Add(key))
                {
                    MessageBox.Show(Window.GetWindow(this), $"Duplicate rule: \"{rule.Phrase.Trim()}\" ({rule.ScopeDisplay}).", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            settings.EnableCustomRemovalRules = EnableCustomRemovalRulesCheckBox.IsChecked == true;
            settings.CustomRemovalRules = _rules
                .Select(r => new PhraseRemovalRule(r.Phrase.Trim(), r.Scope, r.IsEnabled))
                .ToList();
        }

        private void EnableCustomRemovalRulesCheckBox_Changed(object sender, RoutedEventArgs e)
            => UpdateEnabledState();

        private void UpdateEnabledState()
        {
            var enabled = EnableCustomRemovalRulesCheckBox.IsChecked == true;
            RulesGrid.IsEnabled = enabled;
            AddRuleButton.IsEnabled = enabled;
            EditRuleButton.IsEnabled = enabled;
            RemoveRuleButton.IsEnabled = enabled;
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
            var rule = PromptForRule("Add removal rule", null);
            if (rule is null) return;

            if (_rules.Any(r => string.Equals(r.Phrase.Trim(), rule.Phrase.Trim(), StringComparison.OrdinalIgnoreCase)
                                 && r.Scope == rule.Scope))
            {
                MessageBox.Show(Window.GetWindow(this), $"\"{rule.Phrase.Trim()}\" ({rule.ScopeDisplay}) is already in the list.", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _rules.Add(rule);
            RefreshEnableAllState();
        }

        private void EditRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not RemovalRuleItem selected) return;

            var index = _rules.IndexOf(selected);
            var edited = PromptForRule("Edit removal rule", selected);
            if (edited is null) return;

            if (_rules.Where((_, i) => i != index)
                .Any(r => string.Equals(r.Phrase.Trim(), edited.Phrase.Trim(), StringComparison.OrdinalIgnoreCase)
                          && r.Scope == edited.Scope))
            {
                MessageBox.Show(Window.GetWindow(this), $"\"{edited.Phrase.Trim()}\" ({edited.ScopeDisplay}) is already in the list.", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _rules[index] = edited;
            RefreshEnableAllState();
        }

        private void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not RemovalRuleItem selected) return;
            _rules.Remove(selected);
            RefreshEnableAllState();
        }

        // Small modal editor for a rule's phrase and scope. Returns null on cancel.
        private RemovalRuleItem? PromptForRule(string title, RemovalRuleItem? initial)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 340,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var panel = new StackPanel { Margin = new Thickness(12) };

            panel.Children.Add(new TextBlock { Text = "Phrase to remove", Margin = new Thickness(0, 0, 0, 2) });
            var phraseBox = new TextBox { Text = initial?.Phrase ?? string.Empty, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(phraseBox);

            panel.Children.Add(new TextBlock { Text = "Scope", Margin = new Thickness(0, 0, 0, 2) });
            var scopeCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            scopeCombo.Items.Add("Start of sentence");
            scopeCombo.Items.Add("End of sentence");
            scopeCombo.Items.Add("Anywhere");
            scopeCombo.SelectedIndex = (int)(initial?.Scope ?? RemovalScope.Anywhere);
            panel.Children.Add(scopeCombo);

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
            if (phrase.Length == 0) return null;

            return new RemovalRuleItem(phrase, (RemovalScope)scopeCombo.SelectedIndex, enabledCheck.IsChecked == true);
        }
    }

    // View-model wrapper adding a display-friendly scope string for the DataGrid column.
    public class RemovalRuleItem
    {
        public RemovalRuleItem(string phrase, RemovalScope scope, bool isEnabled)
        {
            Phrase = phrase;
            Scope = scope;
            IsEnabled = isEnabled;
        }

        public string Phrase { get; set; }
        public RemovalScope Scope { get; set; }
        public bool IsEnabled { get; set; }

        public string ScopeDisplay => Scope switch
        {
            RemovalScope.StartOfSentence => "Start of sentence",
            RemovalScope.EndOfSentence => "End of sentence",
            _ => "Anywhere"
        };
    }
}
