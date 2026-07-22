using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using VoiceType.Infrastructure.Logging;

namespace VoiceType.UI
{
    /// <summary>
    /// Small clickable "bulb" overlay shown near the mouse cursor after an insertion whose
    /// post-processing changed the transcript. Clicking it raises <see cref="BulbClicked"/> so the
    /// caller can open the comparison popup. Unlike <see cref="BreathingOverlayWindow"/>, this
    /// window is deliberately NOT click-through (it must receive the click) but is still
    /// non-activating so it never steals focus from the target application. It self-dismisses when
    /// the foreground window changes or the user starts typing.
    /// </summary>
    public partial class TranscriptBulbWindow : Window
    {
        // Polling interval for foreground-window-changed dismissal. Cheap and simple; avoids
        // depending on the shared ForegroundWindowMonitor's own dismissal semantics.
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);

        private readonly IntPtr _targetForegroundHwnd;
        private readonly DispatcherTimer _pollTimer;
        private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
        private IntPtr _keyboardHook = IntPtr.Zero;

        /// <summary>Raised when the user clicks the bulb.</summary>
        public event EventHandler? BulbClicked;

        public TranscriptBulbWindow(IntPtr targetForegroundHwnd)
        {
            InitializeComponent();
            _targetForegroundHwnd = targetForegroundHwnd;

            _pollTimer = new DispatcherTimer { Interval = PollInterval };
            _pollTimer.Tick += (_, _) => CheckForegroundChanged();

            SourceInitialized += (_, _) => ApplyNonActivatingStyle();
            Loaded += (_, _) => { _pollTimer.Start(); InstallKeyboardHook(); };
            Closed += (_, _) => { _pollTimer.Stop(); RemoveKeyboardHook(); };
        }

        /// <summary>
        /// Positions the bulb centered on the given screen point (the cursor position), clamped to
        /// the work area so it never renders off-screen.
        /// </summary>
        public void PositionNear(System.Drawing.Point cursorScreenPoint)
        {
            var workArea = SystemParameters.WorkArea;
            double left = cursorScreenPoint.X - (Width / 2);
            double top = cursorScreenPoint.Y - (Height / 2);

            if (left + Width > workArea.Right) left = workArea.Right - Width;
            if (top + Height > workArea.Bottom) top = workArea.Bottom - Height;
            if (left < workArea.Left) left = workArea.Left;
            if (top < workArea.Top) top = workArea.Top;

            Left = left;
            Top = top;
        }

        private void BulbBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            BulbClicked?.Invoke(this, EventArgs.Empty);
        }

        private void CheckForegroundChanged()
        {
            if (_targetForegroundHwnd == IntPtr.Zero) return;
            var current = NativeMethods.GetForegroundWindow();
            // Ignore our own bulb window becoming foreground (e.g. transient click activation).
            var ownHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (current == ownHwnd) return;

            if (current != _targetForegroundHwnd)
            {
                Logger.Info("TranscriptBulbWindow: foreground target changed, dismissing bulb.");
                Close();
            }
        }

        // Adds WS_EX_NOACTIVATE so clicking the bulb never steals focus/activation from the
        // target application, while still allowing the click itself to be received.
        private void ApplyNonActivatingStyle()
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                exStyle |= NativeMethods.WS_EX_NOACTIVATE;
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
            }
            catch (Exception ex)
            {
                Logger.Error($"TranscriptBulbWindow: failed to apply non-activating style: {ex.Message}");
            }
        }

        // Low-level keyboard hook that dismisses the bulb as soon as the user starts typing
        // (any key down while the bulb is visible), following the same WH_KEYBOARD_LL pattern
        // used by GlobalHotkeyManager.
        private void InstallKeyboardHook()
        {
            try
            {
                _keyboardProc = OnKeyboardEvent;
                using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                using var currentModule = currentProcess.MainModule!;
                _keyboardHook = NativeMethods.SetWindowsHookEx(
                    NativeMethods.WH_KEYBOARD_LL,
                    _keyboardProc,
                    NativeMethods.GetModuleHandle(currentModule.ModuleName),
                    0);
            }
            catch (Exception ex)
            {
                Logger.Error($"TranscriptBulbWindow: failed to install keyboard hook: {ex.Message}");
            }
        }

        private IntPtr OnKeyboardEvent(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (int)wParam == NativeMethods.WM_KEYDOWN)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Logger.Info("TranscriptBulbWindow: key press detected, dismissing bulb.");
                    Close();
                }));
            }
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private void RemoveKeyboardHook()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                try { NativeMethods.UnhookWindowsHookEx(_keyboardHook); } catch { }
                _keyboardHook = IntPtr.Zero;
            }
        }

        private static class NativeMethods
        {
            public const int GWL_EXSTYLE = -20;
            public const int WS_EX_NOACTIVATE = 0x08000000;
            public const int WH_KEYBOARD_LL = 13;
            public const int WM_KEYDOWN = 0x0100;

            public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll", SetLastError = true)]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr GetModuleHandle(string lpModuleName);
        }
    }
}
