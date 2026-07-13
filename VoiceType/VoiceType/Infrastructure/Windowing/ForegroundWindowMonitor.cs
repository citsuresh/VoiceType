using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using VoiceType.Infrastructure.Logging;

namespace VoiceType.Infrastructure.Windowing
{
    /// <summary>
    /// Tracks the last "real" foreground window using a low-cost WinEvent hook
    /// (<c>EVENT_SYSTEM_FOREGROUND</c>). This lets tray-toggle dictation restore the window the
    /// user was actually working in, even though single-clicking the tray icon briefly makes the
    /// shell/taskbar (or our own windowless app) the foreground window.
    /// </summary>
    /// <remarks>
    /// Must be created on a thread with a running message pump (the WPF UI thread). The callback
    /// delegate is stored in a field so it is not garbage-collected while the hook is active.
    /// </remarks>
    public sealed class ForegroundWindowMonitor : IDisposable
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private readonly uint _ownProcessId;
        private readonly Native.WinEventDelegate _callback;
        private IntPtr _hook = IntPtr.Zero;

        private IntPtr _lastForegroundHwnd = IntPtr.Zero;
        private IntPtr _lastFocusHwnd = IntPtr.Zero;

        public ForegroundWindowMonitor()
        {
            _ownProcessId = (uint)Environment.ProcessId;
            // Keep a reference to the delegate to prevent it being collected while hooked.
            _callback = OnForegroundChanged;
        }

        /// <summary>The last real foreground window observed (never our own process or the shell).</summary>
        public IntPtr LastForegroundHandle => _lastForegroundHwnd;

        /// <summary>The control that had keyboard focus in the last real foreground window.</summary>
        public IntPtr LastFocusHandle => _lastFocusHwnd;

        /// <summary>Registers the foreground WinEvent hook. Safe to call once.</summary>
        public void Start()
        {
            if (_hook != IntPtr.Zero) return;

            // Seed with whatever is currently in front so we have a value immediately.
            TryRecord(Native.GetForegroundWindow());

            _hook = Native.SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _callback,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            if (_hook == IntPtr.Zero)
            {
                Logger.Error("ForegroundWindowMonitor: SetWinEventHook failed to register.");
            }
            else
            {
                Logger.Info("ForegroundWindowMonitor: foreground hook registered.");
            }
        }

        private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            TryRecord(hwnd);
        }

        private void TryRecord(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            try
            {
                var threadId = Native.GetWindowThreadProcessId(hwnd, out uint processId);
                if (threadId == 0) return;

                // Ignore our own process (tray/overlay windows).
                if (processId == _ownProcessId) return;

                // Ignore the shell (taskbar / desktop) so a stray click on it does not overwrite
                // the user's real target window.
                var className = GetClassName(hwnd);
                if (IsShellWindow(className)) return;

                _lastForegroundHwnd = hwnd;
                _lastFocusHwnd = TryGetFocusedControl(threadId);
            }
            catch (Exception ex)
            {
                Logger.Error($"ForegroundWindowMonitor: failed to record foreground window: {ex}");
            }
        }

        private static bool IsShellWindow(string className)
        {
            return className is "Shell_TrayWnd"
                or "Shell_SecondaryTrayWnd"
                or "WorkerW"
                or "Progman"
                or "NotifyIconOverflowWindow"
                or "TopLevelWindowForOverflowXamlIsland"
                or "Windows.UI.Core.CoreWindow";
        }

        private static IntPtr TryGetFocusedControl(uint threadId)
        {
            var gui = new Native.GUITHREADINFO();
            gui.cbSize = Marshal.SizeOf<Native.GUITHREADINFO>();
            if (Native.GetGUIThreadInfo(threadId, ref gui))
            {
                return gui.hwndFocus;
            }
            return IntPtr.Zero;
        }

        private static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            var length = Native.GetClassName(hwnd, sb, sb.Capacity);
            return length > 0 ? sb.ToString() : string.Empty;
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private static class Native
        {
            public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

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
