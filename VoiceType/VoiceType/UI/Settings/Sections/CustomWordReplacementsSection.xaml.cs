using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Settings for custom word replacements: a global toggle plus a per-rule editable list
    /// mapping mis-heard words/phrases (e.g. names, jargon, acronyms) to a corrected replacement.
    /// Each rule can be enabled/disabled individually without deleting it.
    /// </summary>
    public partial class CustomWordReplacementsSection : UserControl, ISettingsSection
    {
        private readonly ObservableCollection<WordReplacementRule> _rules = new();

        public CustomWordReplacementsSection()
        {
            InitializeComponent();
            RulesGrid.ItemsSource = _rules;
        }

        public string Title => "Custom word replacements";

        public string SearchKeywords =>
            "post-processing custom word replacements dictionary names jargon acronyms corrections";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            EnableCustomWordReplacementsCheckBox.IsChecked = settings.EnableCustomWordReplacements;

            _rules.Clear();
            if (settings.CustomWordReplacements is not null)
            {
                foreach (var rule in settings.CustomWordReplacements)
                    _rules.Add(new WordReplacementRule(rule.From, rule.To, rule.IsEnabled));
            }

            UpdateEnabledState();
        }

        public bool Validate()
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in _rules)
            {
                if (string.IsNullOrWhiteSpace(rule.From))
                {
                    MessageBox.Show(Window.GetWindow(this), "The \"from\" word/phrase cannot be blank.", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (string.IsNullOrEmpty(rule.To))
                {
                    MessageBox.Show(Window.GetWindow(this), $"Rule \"{rule.From.Trim()}\" has no replacement.", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (!seen.Add(rule.From.Trim()))
                {
                    MessageBox.Show(Window.GetWindow(this), $"Duplicate word/phrase: \"{rule.From.Trim()}\".", "VoiceType",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            settings.EnableCustomWordReplacements = EnableCustomWordReplacementsCheckBox.IsChecked == true;
            settings.CustomWordReplacements = _rules
                .Select(r => new WordReplacementRule(r.From.Trim(), r.To, r.IsEnabled))
                .ToList();
        }

        private void EnableCustomWordReplacementsCheckBox_Changed(object sender, RoutedEventArgs e)
            => UpdateEnabledState();

        private void UpdateEnabledState()
        {
            var enabled = EnableCustomWordReplacementsCheckBox.IsChecked == true;
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
            var rule = PromptForRule("Add word replacement", null);
            if (rule is null) return;

            if (_rules.Any(r => string.Equals(r.From.Trim(), rule.From.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(Window.GetWindow(this), $"\"{rule.From.Trim()}\" is already in the list.", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _rules.Add(rule);
            RefreshEnableAllState();
        }

        private void EditRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not WordReplacementRule selected) return;

            var index = _rules.IndexOf(selected);
            var edited = PromptForRule("Edit word replacement", selected);
            if (edited is null) return;

            if (_rules.Where((_, i) => i != index)
                .Any(r => string.Equals(r.From.Trim(), edited.From.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(Window.GetWindow(this), $"\"{edited.From.Trim()}\" is already in the list.", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _rules[index] = edited;
            RefreshEnableAllState();
        }

        private void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is not WordReplacementRule selected) return;
            _rules.Remove(selected);
            RefreshEnableAllState();
        }

        // Small modal editor for a rule's "from" word/phrase and "to" replacement. Returns null on cancel.
        private WordReplacementRule? PromptForRule(string title, WordReplacementRule? initial)
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

            panel.Children.Add(new TextBlock { Text = "From (mis-heard word/phrase)", Margin = new Thickness(0, 0, 0, 2) });
            var fromBox = new TextBox { Text = initial?.From ?? string.Empty, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(fromBox);

            panel.Children.Add(new TextBlock { Text = "To (replacement)", Margin = new Thickness(0, 0, 0, 2) });
            var toBox = new TextBox { Text = initial?.To ?? string.Empty, Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(toBox);

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
            fromBox.Focus();

            if (dialog.ShowDialog() != true) return null;

            var from = fromBox.Text.Trim();
            var to = toBox.Text;
            if (from.Length == 0 || string.IsNullOrEmpty(to)) return null;

            return new WordReplacementRule(from, to, enabledCheck.IsChecked == true);
        }
    }
}
