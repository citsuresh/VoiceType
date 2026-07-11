using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace VoiceType.UI.ViewModels
{
    public enum WaveformRenderMode
    {
        Bars,
        Polyline,
        ScrollingBitmap,
        Spectrogram
    }

    public class FloatingOverlayViewModel : INotifyPropertyChanged
    {
        private bool _isVisible;
        private string _statusText = "Idle";
        private string _previewText = string.Empty;
        private string _modelName = string.Empty;
        private double _audioLevel;
        private bool _autoScroll = true;
        private int _maxWaveformPoints = 80;
        private double _maxWaveformBarHeight = 64;
        private ObservableCollection<double> _waveformPoints = new ObservableCollection<double>();
        private WaveformRenderMode _selectedRenderMode = WaveformRenderMode.ScrollingBitmap;
        private double _latestValue;

        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public string PreviewText
        {
            get => _previewText;
            set { _previewText = value; OnPropertyChanged(); }
        }

        public string ModelName
        {
            get => _modelName;
            set { _modelName = value; OnPropertyChanged(); }
        }

        public double AudioLevel
        {
            get => _audioLevel;
            set { _audioLevel = value; OnPropertyChanged(); }
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set { _autoScroll = value; OnPropertyChanged(); }
        }

        public ObservableCollection<double> WaveformPoints
        {
            get => _waveformPoints;
            set { _waveformPoints = value; OnPropertyChanged(); }
        }

        // selected renderer mode (Bars, Polyline, ScrollingBitmap, Spectrogram)
        public WaveformRenderMode SelectedRenderMode
        {
            get => _selectedRenderMode;
            set { _selectedRenderMode = value; OnPropertyChanged(); }
        }

        // latest display-normalized value (0..1) pushed by controller
        public double LatestValue
        {
            get => _latestValue;
            set { _latestValue = value; OnPropertyChanged(); }
        }

        // Point collection used to render a continuous polyline waveform (faster than many individual bars)
        public PointCollection WaveformPolyline { get; } = new PointCollection();

        public int MaxWaveformPoints
        {
            get => _maxWaveformPoints;
            set { _maxWaveformPoints = value; OnPropertyChanged(); }
        }

        // maximum visual height (pixels) for individual waveform bars
        public double MaxWaveformBarHeight
        {
            get => _maxWaveformBarHeight;
            set { _maxWaveformBarHeight = value; OnPropertyChanged(); }
        }

        // derived diagnostic amplitude values
        private double _lastAmplitude;
        public double LastAmplitude
        {
            get => _lastAmplitude;
            set { _lastAmplitude = value; OnPropertyChanged(); }
        }

        private double _lastRms;
        public double LastRms
        {
            get => _lastRms;
            set { _lastRms = value; OnPropertyChanged(); }
        }

        private double _lastAmplitudeScaled;
        public double LastAmplitudeScaled
        {
            get => _lastAmplitudeScaled;
            set { _lastAmplitudeScaled = value; OnPropertyChanged(); }
        }
        private double _lastAmplitudeDb;
        public double LastAmplitudeDb
        {
            get => _lastAmplitudeDb;
            set { _lastAmplitudeDb = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
