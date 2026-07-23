using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VoiceType.Infrastructure.Logging;

namespace VoiceType.UI
{
    /// <summary>
    /// Compact pill overlay with a Wispr-Flow-style traveling-wave waveform.
    /// All bars share the same frequency with evenly-spaced phases, producing
    /// a smooth left-to-right sine wave. The caller only needs to set
    /// <see cref="CurrentAmplitude"/> (0..1).
    /// </summary>
    public partial class BreathingOverlayWindow : Window
    {
        private const int BarCount = 10;
        private const double MaxBarHeight = 26.0;
        private const double MinBarHeight = 3.0;

        // Animated wave-dots (used for "Starting mic" and "Processing"): dots bob up and down
        // as a smooth traveling wave.
        private const int DotWaveCount = 5;
        private const double DotWaveAmplitude = 6.0; // vertical travel in device-independent pixels
        private const double DotWaveSpeed = 7.0;     // radians per second

        // Per-bar independent oscillation parameters (set once, never change).
        private readonly double[] _phases   = new double[BarCount];
        private readonly double[] _freqs    = new double[BarCount];
        // Gaussian bell-curve envelope: center bars taller, edge bars shorter (set once).
        private readonly double[] _envelope = new double[BarCount];
        // Per-bar current smoothed heights.
        private readonly double[] _heights  = new double[BarCount];

        private readonly ObservableCollection<double> _bars = new();
        private readonly DispatcherTimer _timer;

        // Thread-safe amplitude: written from audio thread, read on UI timer thread.
        private long _masterLevelBits;
        // Smoothed display level, updated only on the UI thread inside OnTick.
        private double _displayLevel;

        // Processing state: when true the pill shows animated "Processing" text with wave dots.
        private bool _processing;

        // Preparing state: when true the pill shows "Starting mic" with wave-like bobbing dots.
        private bool _preparing;
        // Wave dots bound to the DotsWave ItemsControl. Each item raises PropertyChanged so its
        // TranslateTransform.Y updates in place every tick (used for both preparing and processing).
        private readonly ObservableCollection<WaveDot> _dotOffsets = new();

        public BreathingOverlayWindow()
        {
            InitializeComponent();

            // Evenly-spaced phases produce a smooth left-to-right traveling wave.
            for (int i = 0; i < BarCount; i++)
            {
                _phases[i]  = 2.0 * Math.PI * i / BarCount;
                _freqs[i]   = 4.0; // all bars share the same speed → smooth traveling wave
                _heights[i] = MinBarHeight;
                _bars.Add(MinBarHeight);
            }

            // Gaussian bell curve: edges ~40% of center height, matching Wispr Flow style.
            double envCenter = (BarCount - 1) / 2.0;
            double envSigma  = BarCount / 3.0;
            for (int i = 0; i < BarCount; i++)
            {
                double d = (i - envCenter) / envSigma;
                _envelope[i] = Math.Exp(-0.5 * d * d);
            }

            BarItems.ItemsSource = _bars;

            for (int i = 0; i < DotWaveCount; i++)
            {
                _dotOffsets.Add(new WaveDot());
            }
            DotsWave.ItemsSource = _dotOffsets;

            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 Hz
            };
            _timer.Tick += OnTick;

            Loaded += (_, _) => { PositionBottomCenter(); _timer.Start(); };
            Closed += (_, _) => _timer.Stop();

            // Make the pill click-through and non-activating so it never steals focus from the
            // app the user is dictating into. Applied once the HWND exists.
            SourceInitialized += (_, _) => ApplyClickThroughStyles();
        }

        // Adds WS_EX_TRANSPARENT (mouse events pass through to the window beneath) and
        // WS_EX_NOACTIVATE (the window never becomes active / steals focus) to the pill.
        private void ApplyClickThroughStyles()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE;
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
            }
            catch (Exception ex)
            {
                Logger.Error($"BreathingOverlayWindow: failed to apply click-through styles: {ex.Message}");
            }
        }

        /// <summary>
        /// Feed the current normalised amplitude (0..1) from the audio pipeline.
        /// Safe to call from any thread.
        /// </summary>
        public double CurrentAmplitude
        {
            set
            {
                var v = Math.Max(0.0, Math.Min(1.0, value));
                Interlocked.Exchange(ref _masterLevelBits, BitConverter.DoubleToInt64Bits(v));
            }
        }

        /// <summary>
        /// Applies the configured pill background color and opacity from settings. Falls back to
        /// the default look if the configured color string fails to parse. Must be called on the
        /// UI thread.
        /// </summary>
        public void ApplyAppearance(Infrastructure.Config.VoiceTypeSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            var brush = PillAppearance.CreateBrush(settings);
            Pill.Background = brush;
            ModelBubble.Background = brush;
        }

        /// <summary>Positions the window at the bottom-center of the primary screen.</summary>
        public void PositionBottomCenter()
        {
            var screen = SystemParameters.WorkArea;
            Left = screen.Left + (screen.Width  - ActualWidth)  / 2;
            Top  = screen.Top  +  screen.Height - ActualHeight  - 24;
        }

        /// <summary>
        /// Forces the SizeToContent window to re-measure synchronously after its content has
        /// changed, then re-centers. Doing this synchronously (rather than via a deferred
        /// dispatcher callback) ensures the transparent window is correctly sized BEFORE the next
        /// frame renders, avoiding a visible "dots then text" pop when switching states.
        /// </summary>
        private void ResizeToContentAndCenter()
        {
            SizeToContent = SizeToContent.Manual;
            SizeToContent = SizeToContent.WidthAndHeight;
            UpdateLayout();
            PositionBottomCenter();
        }

        /// <summary>
        /// Sets the read-only model-name bubble shown above the pill. Pass a null/empty value
        /// to hide the bubble. Must be called on the UI thread.
        /// </summary>
        public void SetModelName(string? modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                ModelBubble.Visibility = Visibility.Collapsed;
                return;
            }

            ModelText.Text = modelName;
            ModelBubble.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Switches the pill into a "preparing" state shown while the microphone is warming up:
        /// hides the waveform bars and shows "<paramref name="text"/>" followed by dots that bob
        /// up and down as a traveling wave. Call <see cref="ShowListening"/> once real audio arrives.
        /// Must be called on the UI thread.
        /// </summary>
        public void ShowPreparing(string text = "Starting mic")
        {
            _preparing = true;
            _processing = false;

            StatusText.Text = string.IsNullOrWhiteSpace(text) ? "Starting mic" : text;
            DotsWave.Visibility = Visibility.Visible;
            BarItems.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Visible;

            // Size the window to fit the new content synchronously, then re-center.
            ResizeToContentAndCenter();
        }

        /// <summary>
        /// Switches the pill back to the listening waveform: hides any status text and shows the bars.
        /// Call this when the microphone is truly live (e.g. the first audio buffer arrived).
        /// Must be called on the UI thread.
        /// </summary>
        public void ShowListening()
        {
            _preparing = false;
            _processing = false;

            DotsWave.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Collapsed;
            BarItems.Visibility = Visibility.Visible;

            // Size the window to fit the new content synchronously, then re-center.
            ResizeToContentAndCenter();
        }

        /// <summary>
        /// Switches the pill into a processing state: hides the waveform bars and shows a
        /// "<paramref name="text"/>" label with the same wave-like bobbing dots used while
        /// preparing, until the window is closed. Must be called on the UI thread.
        /// </summary>
        public void ShowProcessing(string text = "Processing")
        {
            _processing = true;
            _preparing = false;
            StatusText.Text = string.IsNullOrWhiteSpace(text) ? "Processing" : text;
            DotsWave.Visibility = Visibility.Visible;
            BarItems.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Visible;

            // Size the window to fit the new content synchronously, then re-center.
            ResizeToContentAndCenter();
        }

        /// <summary>
        /// Switches the pill into a static message state (no animation), showing a short
        /// message such as a clipboard-fallback notice. The window auto-closes after
        /// <paramref name="autoCloseMs"/> milliseconds. Must be called on the UI thread.
        /// </summary>
        public void ShowMessage(string text, int autoCloseMs = 5000)
        {
            _processing = false;
            _preparing = false;
            _timer.Stop();

            StatusText.Text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
            // Show the full message: no trimming/wrapping so the pill grows horizontally to fit
            // (the pill has a fixed height, so wrapping would clip vertically).
            StatusText.TextTrimming = TextTrimming.None;
            StatusText.TextWrapping = TextWrapping.NoWrap;
            StatusText.MaxWidth = double.PositiveInfinity;
            // Horizontal breathing room so the message does not touch the pill's rounded corners.
            // Applied here (message-only window) so it does not affect the "Starting mic"/"Processing"
            // dot spacing on the dictation pill.
            StatusText.Padding = new Thickness(14, 0, 14, 0);
            DotsWave.Visibility = Visibility.Collapsed;
            BarItems.Visibility = Visibility.Collapsed;
            StatusPanel.Visibility = Visibility.Visible;
            // Standalone message pill has no model context, so hide the (otherwise empty) model
            // bubble that would otherwise render as a small empty blue circle above the pill.
            ModelBubble.Visibility = Visibility.Collapsed;

            // Size the window to fit the message synchronously, then re-center. Using the same
            // synchronous path as the other states ensures the pill actually expands to fit the
            // text instead of keeping its previous (narrow) width.
            _timer.Stop();
            ResizeToContentAndCenter();

            if (autoCloseMs > 0)
            {
                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(autoCloseMs) };
                closeTimer.Tick += (_, _) =>
                {
                    closeTimer.Stop();
                    try { Close(); } catch { }
                };
                closeTimer.Start();
            }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_preparing || _processing)
            {
                UpdateWaveDots();
                return;
            }

            var t      = DateTime.UtcNow.TimeOfDay.TotalSeconds;
            var master = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _masterLevelBits));

            // Fast attack (follows voice immediately), slower decay (bars don't snap to zero).
            _displayLevel = master > _displayLevel
                ? master
                : _displayLevel * 0.78;

            for (int i = 0; i < BarCount; i++)
            {
                // Traveling-wave sine: same frequency, evenly-spaced phases.
                // |sin| keeps height always positive; range 0..1.
                var osc = Math.Abs(Math.Sin(t * _freqs[i] + _phases[i]));

                // Bell-curve envelope scales max height per bar (center tall, edges short).
                var effectiveMax = MaxBarHeight * _envelope[i];

                // When silent: each bar pulses gently at ~10% of effectiveMax.
                // When loud:   bars pulse between 30% and 100% of effectiveMax.
                var idleHeight   = MinBarHeight + 0.10 * effectiveMax * osc;
                var activeHeight = MinBarHeight + _displayLevel * effectiveMax * (0.3 + 0.7 * osc);
                var target = idleHeight + _displayLevel * (activeHeight - idleHeight);

                // Smooth each bar: fast attack, moderate decay — independent per bar.
                _heights[i] = _heights[i] < target
                    ? _heights[i] + (target - _heights[i]) * 0.80
                    : _heights[i] + (target - _heights[i]) * 0.35;

                _bars[i] = Math.Max(MinBarHeight, _heights[i]);
            }
        }

        // Bobs each dot up and down using evenly-spaced phases so the row of dots forms a smooth
        // left-to-right traveling wave. Updates each WaveDot.Offset in place (PropertyChanged),
        // which drives the per-dot TranslateTransform.Y without regenerating the item containers.
        private void UpdateWaveDots()
        {
            var t = DateTime.UtcNow.TimeOfDay.TotalSeconds;
            for (int i = 0; i < _dotOffsets.Count; i++)
            {
                var phase = 2.0 * Math.PI * i / DotWaveCount;
                // Negative so the dot travels upward from its baseline; range -amp..0.
                _dotOffsets[i].Offset = -DotWaveAmplitude * (0.5 + 0.5 * Math.Sin(t * DotWaveSpeed + phase));
            }
        }
    }

    /// <summary>
    /// A single animated wave dot. Raises <see cref="INotifyPropertyChanged"/> so the bound
    /// <c>TranslateTransform.Y</c> updates in place each tick, producing a smooth bobbing motion.
    /// </summary>
    internal sealed class WaveDot : INotifyPropertyChanged
    {
        private double _offset;

        public double Offset
        {
            get => _offset;
            set
            {
                if (_offset == value) return;
                _offset = value;
                PropertyChanged?.Invoke(this, OffsetChangedArgs);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private static readonly PropertyChangedEventArgs OffsetChangedArgs = new(nameof(Offset));
    }

    /// <summary>
    /// Native interop for making the overlay click-through and non-activating.
    /// </summary>
    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
