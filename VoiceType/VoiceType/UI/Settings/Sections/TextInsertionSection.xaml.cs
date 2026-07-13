using System;
using System.Windows.Controls;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Text-insertion settings: paste vs typing, clipboard restore, and clipboard-fallback behavior
    /// when no editable target is focused.
    /// </summary>
    public partial class TextInsertionSection : UserControl, ISettingsSection
    {
        public TextInsertionSection()
        {
            InitializeComponent();
        }

        public string Title => "Text insertion";

        public string SearchKeywords => "insert method paste typing clipboard restore copy notification editable";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            InsertMethodComboBox.ItemsSource = new[] { "Clipboard", "Typing" };
            InsertMethodComboBox.SelectedItem =
                string.Equals(settings.InsertMethod, "Typing", StringComparison.OrdinalIgnoreCase) ? "Typing" : "Clipboard";

            EnableClipboardRestoreCheckBox.IsChecked = settings.EnableClipboardRestore;
            CopyToClipboardWhenNoEditableCheckBox.IsChecked = settings.CopyToClipboardWhenNoEditable;
            ShowClipboardCopyNotificationCheckBox.IsChecked = settings.ShowClipboardCopyNotification;
        }

        public bool Validate() => true;

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            settings.InsertMethod = InsertMethodComboBox.SelectedItem as string ?? "Clipboard";
            settings.EnableClipboardRestore = EnableClipboardRestoreCheckBox.IsChecked == true;
            settings.CopyToClipboardWhenNoEditable = CopyToClipboardWhenNoEditableCheckBox.IsChecked == true;
            settings.ShowClipboardCopyNotification = ShowClipboardCopyNotificationCheckBox.IsChecked == true;
        }
    }
}
