using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceType.Infrastructure.Config;

namespace VoiceType.UI.Settings.Sections
{
    /// <summary>
    /// Appearance settings: floating waveform pill color and transparency, with a live preview.
    /// </summary>
    public partial class AppearanceSection : UserControl, ISettingsSection
    {
        private static readonly string[] PresetColors =
        {
            "#283593", "#111118", "#1B5E20", "#B71C1C", "#4A148C", "#004D40", "#37474F"
        };

        // Sample bar heights approximating the real waveform look for the static preview.
        private static readonly double[] PreviewBarHeights = { 6, 12, 20, 28, 18, 24, 14, 8, 16, 10 };

        // Guards against re-entrant preview updates while programmatically setting controls.
        private bool _isLoading;

        public AppearanceSection()
        {
            InitializeComponent();
            BuildPreviewBars();
            BuildPresetSwatches();
        }

        public string Title => "Appearance";

        public string SearchKeywords => "pill color colour waveform overlay transparency opacity theme preview";

        public void Load(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            _isLoading = true;
            try
            {
                ColorHexTextBox.Text = settings.PillColor ?? PillAppearance.DefaultColor;
                OpacitySlider.Value = Math.Clamp(settings.PillOpacity, 0.2, 1.0) * 100.0;
            }
            finally
            {
                _isLoading = false;
            }

            UpdatePreview();
        }

        public bool Validate()
        {
            if (PillAppearance.ParseColor(ColorHexTextBox.Text) is null)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "Pill color must be a valid hex color, e.g. #283593.", "VoiceType",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        public void Save(VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            settings.PillColor = ColorHexTextBox.Text?.Trim() ?? PillAppearance.DefaultColor;
            settings.PillOpacity = OpacitySlider.Value / 100.0;
        }

        private void BuildPreviewBars()
        {
            var points = new System.Collections.ObjectModel.ObservableCollection<double>();
            foreach (var h in PreviewBarHeights)
                points.Add(h);
            PreviewBars.ItemsSource = points;
        }

        private void BuildPresetSwatches()
        {
            foreach (var hex in PresetColors)
            {
                var color = (Color)ColorConverter.ConvertFromString(hex)!;
                var swatch = new Border
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(4),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(color),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = hex
                };
                swatch.MouseLeftButtonUp += PresetSwatch_MouseLeftButtonUp;
                PresetSwatches.Items.Add(swatch);
            }
        }

        private void PresetSwatch_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border { Tag: string hex })
                ColorHexTextBox.Text = hex;
        }

        /// <summary>
        /// Opens the system color-picker dialog (Windows Forms' <see cref="System.Windows.Forms.ColorDialog"/>,
        /// which offers full RGB/hue-saturation selection plus custom colors) and writes the
        /// chosen color into <see cref="ColorHexTextBox"/> as a "#RRGGBB" hex string.
        /// </summary>
        private void ColorSwatch_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var initial = PillAppearance.ParseColor(ColorHexTextBox.Text);

            using var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true
            };

            if (initial is Color c)
                dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var picked = dialog.Color;
                ColorHexTextBox.Text = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            }
        }

        private void ColorHexTextBox_TextChanged(object sender, TextChangedEventArgs e)
            => UpdatePreview();

        private void PreviewBackgroundRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (PreviewBackdrop is null)
                return;

            PreviewBackdrop.Background = PreviewBackgroundWhiteRadio.IsChecked == true
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
            => UpdatePreview();

        private void UpdatePreview()
        {
            if (_isLoading || PreviewPill is null || OpacityValueText is null)
                return;

            var opacity = OpacitySlider.Value / 100.0;
            OpacityValueText.Text = $"{OpacitySlider.Value:0}%";

            PreviewPill.Background = PillAppearance.CreateBrush(ColorHexTextBox.Text, opacity);

            var isValid = PillAppearance.ParseColor(ColorHexTextBox.Text) is not null;
            ColorHexTextBox.Background = isValid ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xE0));

            if (ColorSwatch is not null)
                ColorSwatch.Background = isValid
                    ? new SolidColorBrush(PillAppearance.ParseColor(ColorHexTextBox.Text)!.Value)
                    : Brushes.Transparent;
        }
    }
}
