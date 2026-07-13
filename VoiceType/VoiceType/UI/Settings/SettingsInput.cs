using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VoiceType.UI.Settings
{
    /// <summary>
    /// Shared input helpers used across settings sections: numeric/port validation with a
    /// user-facing warning, and an executable file picker.
    /// </summary>
    internal static class SettingsInput
    {
        /// <summary>
        /// Parses a required positive (&gt; 0) integer, showing a validation message and returning
        /// false when the value is missing or invalid.
        /// </summary>
        public static bool TryParsePositiveInt(DependencyObject owner, string? text, string fieldName, out int value)
        {
            if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
                return true;

            MessageBox.Show(Window.GetWindow(owner), $"{fieldName} must be a positive whole number.", "VoiceType",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        /// <summary>
        /// Parses a required TCP port (1-65535), showing a validation message on failure.
        /// </summary>
        public static bool TryParsePort(DependencyObject owner, string? text, out int value)
        {
            if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                value is >= 1 and <= 65535)
                return true;

            MessageBox.Show(Window.GetWindow(owner), "Server port must be between 1 and 65535.", "VoiceType",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            value = 0;
            return false;
        }

        /// <summary>
        /// Opens a file picker for an .exe and writes the chosen path into <paramref name="target"/>.
        /// </summary>
        public static void BrowseForExecutable(TextBox target)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select executable",
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };

            var current = target.Text;
            if (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(current));
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                        dialog.InitialDirectory = dir;
                }
                catch { }
            }

            if (dialog.ShowDialog(Window.GetWindow(target)) == true)
                target.Text = dialog.FileName;
        }
    }
}
