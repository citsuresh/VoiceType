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

        /// <summary>Positions the window at the bottom-center of the primary screen.</summary>
        public void PositionBottomCenter()
        {
            var screen = SystemParameters.WorkArea;
            Left = screen.Left + (screen.Width - ActualWidth) / 2;
            Top = screen.Top + screen.Height - ActualHeight - 24;
        }
    }
}
