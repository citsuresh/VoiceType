using System;
using System.Windows;

namespace VoiceType.UI
{
    public partial class CompactOverlayWindow : Window
    {
        public CompactOverlayWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PositionBottomCenter();
        }

        /// <summary>
        /// Applies the configured pill background color and opacity from settings. Falls back to
        /// the default look if the configured color string fails to parse. Must be called on the
        /// UI thread.
        /// </summary>
        public void ApplyAppearance(Infrastructure.Config.VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Pill.Background = PillAppearance.CreateBrush(settings);
        }

        /// <summary>Positions the window at the bottom-center of the primary screen.</summary>
        public void PositionBottomCenter()
        {
            var screen = SystemParameters.WorkArea;
            Left = screen.Left + (screen.Width - ActualWidth) / 2;
            Top = screen.Top + screen.Height - ActualHeight - 24;
        }
    }
}
