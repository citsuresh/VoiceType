using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VoiceType.Infrastructure.Logging;

namespace VoiceType.Infrastructure.Windowing
{
    // Captures the current foreground window handle and can attempt to restore it later.
    public class ForegroundWindowTracker
    {
        private IntPtr _capturedHwnd = IntPtr.Zero;
        private IntPtr _capturedFocusHwnd = IntPtr.Zero;

        public void CaptureForegroundWindow()
        {
            _capturedHwnd = Native.GetForegroundWindow();
            _capturedFocusHwnd = TryGetFocusedControl(_capturedHwnd);
            Logger.Info($"Captured foreground window: {DescribeWindow(_capturedHwnd)}; focused control: {DescribeWindow(_capturedFocusHwnd)}");
        }

        public IntPtr CapturedHandle => _capturedHwnd;

        // Seeds the tracker from a previously observed window handle instead of the live
        // GetForegroundWindow() result. Used for tray-toggle dictation, where clicking the tray
        // icon makes the shell/taskbar the foreground window, so the live value would be wrong.
        // Falls back to live capture when the supplied handle is not a valid window.
        public void CaptureFromHandle(IntPtr hwnd, IntPtr focusHwnd = default)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd))
            {
                Logger.Info("CaptureFromHandle: supplied handle invalid; falling back to live foreground capture.");
                CaptureForegroundWindow();
                return;
            }

            _capturedHwnd = hwnd;
            _capturedFocusHwnd = focusHwnd != IntPtr.Zero && Native.IsWindow(focusHwnd)
                ? focusHwnd
                : TryGetFocusedControl(hwnd);
            Logger.Info($"Captured foreground window from cached handle: {DescribeWindow(_capturedHwnd)}; focused control: {DescribeWindow(_capturedFocusHwnd)}");
        }

        // The control that had keyboard focus in the captured window (updated after restore).
        public IntPtr CapturedFocusHandle => _capturedFocusHwnd;

        // Returns the handle of the control that currently has keyboard focus within the
        // given top-level window's thread. Works across processes via GetGUIThreadInfo.
        private static IntPtr TryGetFocusedControl(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                var threadId = Native.GetWindowThreadProcessId(hwnd, IntPtr.Zero);
                if (threadId == 0) return IntPtr.Zero;

                var gui = new Native.GUITHREADINFO();
                gui.cbSize = Marshal.SizeOf<Native.GUITHREADINFO>();
                if (Native.GetGUIThreadInfo(threadId, ref gui))
                {
                    return gui.hwndFocus;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to capture focused control: {ex}");
            }
            return IntPtr.Zero;
        }

        // Returns true if the captured window is currently the foreground window.
        public bool IsCapturedWindowForeground()
        {
            return _capturedHwnd != IntPtr.Zero && Native.GetForegroundWindow() == _capturedHwnd;
        }

        // Attempts to restore the previously captured window to foreground.
        // Uses AttachThreadInput to bypass the OS foreground-lock that otherwise
        // causes SetForegroundWindow to fail silently for background processes.
        public bool TryRestoreForegroundWindow()
        {
            if (_capturedHwnd == IntPtr.Zero) return false;

            try
            {
                if (Native.IsIconic(_capturedHwnd))
                {
                    Native.ShowWindow(_capturedHwnd, Native.SW_RESTORE);
                }

                // First attempt: direct call (works when we still hold foreground rights).
                if (Native.SetForegroundWindow(_capturedHwnd) && IsCapturedWindowForeground())
                {
                    RestoreFocusedControl();
                    return true;
                }

                // Fallback: attach our input thread to the target window's thread so the
                // OS treats the SetForegroundWindow call as coming from the active app.
                var foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero);
                var targetThread = Native.GetWindowThreadProcessId(_capturedHwnd, IntPtr.Zero);
                var currentThread = Native.GetCurrentThreadId();

                bool attachedToForeground = false;
                bool attachedToTarget = false;
                try
                {
                    if (foregroundThread != 0 && foregroundThread != currentThread)
                        attachedToForeground = Native.AttachThreadInput(currentThread, foregroundThread, true);
                    if (targetThread != 0 && targetThread != currentThread)
                        attachedToTarget = Native.AttachThreadInput(currentThread, targetThread, true);

                    Native.ShowWindow(_capturedHwnd, Native.SW_SHOW);
                    Native.BringWindowToTop(_capturedHwnd);
                    Native.SetForegroundWindow(_capturedHwnd);
                }
                finally
                {
                    if (attachedToForeground) Native.AttachThreadInput(currentThread, foregroundThread, false);
                    if (attachedToTarget) Native.AttachThreadInput(currentThread, targetThread, false);
                }

                // Put keyboard focus back on the exact control that had it at capture time.
                // Done after detaching above; RestoreFocusedControl manages its own attach.
                RestoreFocusedControl();

                return IsCapturedWindowForeground();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to restore foreground window: {ex}");
                return false;
            }
        }

        // Restores keyboard focus to the exact control that was focused at capture time.
        // Keyboard focus is per-thread, so cross-thread SetFocus only works while our thread
        // is attached to the target control's thread input. This method attaches, sets focus,
        // and detaches so it is correct regardless of the caller's attach state.
        private void RestoreFocusedControl()
        {
            if (_capturedFocusHwnd == IntPtr.Zero) return;
            if (!Native.IsWindow(_capturedFocusHwnd)) return;

            var targetThread = Native.GetWindowThreadProcessId(_capturedFocusHwnd, IntPtr.Zero);
            var currentThread = Native.GetCurrentThreadId();
            bool attached = false;
            try
            {
                if (targetThread != 0 && targetThread != currentThread)
                    attached = Native.AttachThreadInput(currentThread, targetThread, true);

                var result = Native.SetFocus(_capturedFocusHwnd);
                Logger.Info($"RestoreFocusedControl: SetFocus({DescribeWindow(_capturedFocusHwnd)}) attached={attached}, returned=0x{result.ToInt64():X}, currentFocus={DescribeWindow(Native.GetFocus())}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to restore focused control: {ex}");
            }
            finally
            {
                if (attached) Native.AttachThreadInput(currentThread, targetThread, false);
            }
        }

        // Attempts to restore the captured window and verifies it actually became foreground,
        // retrying a few times with a short delay between attempts. Returns true only when the
        // captured window is confirmed as the current foreground window.
        public async Task<bool> RestoreAndVerifyAsync(int maxAttempts = 5, int delayMs = 60, CancellationToken cancellationToken = default)
        {
            if (_capturedHwnd == IntPtr.Zero)
            {
                Logger.Error("RestoreAndVerifyAsync: no captured window handle to restore.");
                return false;
            }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryRestoreForegroundWindow() && IsCapturedWindowForeground())
                {
                    Logger.Info($"Foreground restored to {DescribeWindow(_capturedHwnd)} on attempt {attempt}.");
                    return true;
                }

                try { await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { throw; }
            }

            Logger.Error($"Foreground restore failed after {maxAttempts} attempts. Captured={DescribeWindow(_capturedHwnd)}, Current={DescribeWindow(Native.GetForegroundWindow())}");
            return false;
        }

        private static string DescribeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "(none)";
            try
            {
                var sb = new StringBuilder(256);
                Native.GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                return string.IsNullOrWhiteSpace(title) ? $"hwnd=0x{hwnd.ToInt64():X} (no title)" : $"'{title}' (hwnd=0x{hwnd.ToInt64():X})";
            }
            catch
            {
                return $"hwnd=0x{hwnd.ToInt64():X}";
            }
        }

        private static class Native
        {
            public const int SW_RESTORE = 9;
            public const int SW_SHOW = 5;

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsIconic(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool BringWindowToTop(IntPtr hWnd);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

            [DllImport("kernel32.dll")]
            public static extern uint GetCurrentThreadId();

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

            [DllImport("user32.dll")]
            public static extern IntPtr SetFocus(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern IntPtr GetFocus();

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct GUITHREADINFO
            {
                public int cbSize;
                public uint flags;
                public IntPtr hwndActive;
                public IntPtr hwndFocus;
                public IntPtr hwndCapture;
                public IntPtr hwndMenuOwner;
                public IntPtr hwndMoveSize;
                public IntPtr hwndCaret;
                public RECT rcCaret;
            }
        }
    }
}
