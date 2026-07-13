using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Threading;
using System.Threading.Tasks;
using VoiceType.Infrastructure.Config;

namespace VoiceType.Infrastructure.Hotkeys
{
    // Identifies which configured hotkey a GlobalHotkeyManager instance is bound to.
    public enum HotkeyKind
    {
        // Hold-to-talk dictation hotkey (DictationHotkey). Fires press and release.
        Dictation,
        // Toggle-mode hotkey (ToggleHotkey). A single tap toggles a hands-free session.
        Toggle
    }

    // A lightweight global hotkey manager using a low-level keyboard hook (WH_KEYBOARD_LL).
    // This approach is used because WM_HOTKEY does not reliably provide press-and-hold semantics
    // (detecting both key down and key up) for all scenarios. The low-level hook allows
    // detecting keydown and keyup for the configured key and checking modifier state.
    public class GlobalHotkeyManager : IDisposable
    {
        private readonly VoiceTypeSettings _settings;
        private readonly HotkeyKind _kind;
        private IntPtr _hookId = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc _proc;
        // Virtual key code of the configured target key. Not readonly so the hotkey can be
        // re-registered live (see UpdateHotkey) when the user changes it in Settings.
        private int _targetVk;

        // The configured target key name for this manager's hotkey kind.
        private string ConfiguredKey =>
            _kind == HotkeyKind.Toggle ? _settings.ToggleHotkeyKey : _settings.HotkeyKey;

        // The configured modifier list for this manager's hotkey kind.
        private string ConfiguredModifiers =>
            _kind == HotkeyKind.Toggle ? _settings.ToggleHotkeyModifiers : _settings.HotkeyModifiers;

        private bool _isDisposed = false;

        // True while we are suppressing the target hotkey key from the moment a matching
        // combo key-down is seen until its key-up, so the corresponding key-up is also
        // suppressed and never reaches the focused application.
        private bool _suppressingTarget = false;

        // When true, the hook stays installed but hotkey activations are ignored. Used to
        // silence the dictation hotkey while the Settings window is open (so capturing a new
        // combo, e.g. Ctrl+Alt, doesn't also start a dictation session).
        public bool IsSuspended { get; set; }

        // Events raised when hotkey is pressed (down) and released (up)
        public event EventHandler? HotkeyPressed;
        public event EventHandler? HotkeyReleased;

        public GlobalHotkeyManager(VoiceTypeSettings settings)
            : this(settings, HotkeyKind.Dictation)
        {
        }

        public GlobalHotkeyManager(VoiceTypeSettings settings, HotkeyKind kind)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _kind = kind;

            // Parse configured key into a virtual key code
            if (!Enum.TryParse<Key>(ConfiguredKey ?? string.Empty, out var key))
            {
                // fallback to LeftAlt
                key = Key.LeftAlt;
            }

            _targetVk = KeyInterop.VirtualKeyFromKey(key);
            _proc = HookCallback;
        }

        public void Start()
        {
            _hookId = NativeMethods.SetHook(_proc);
        }

        /// <summary>
        /// Re-reads the configured hotkey key from settings and updates the cached target
        /// virtual-key code so a change made in Settings takes effect immediately without
        /// restarting the app or re-installing the keyboard hook. Modifiers are already read
        /// live in <see cref="ModifiersMatch"/>, so only the target key needs refreshing.
        /// </summary>
        public void UpdateHotkey()
        {
            if (!Enum.TryParse<Key>(ConfiguredKey ?? string.Empty, out var key))
            {
                key = Key.LeftAlt;
            }

            _targetVk = KeyInterop.VirtualKeyFromKey(key);
            // Reset any in-progress suppression so a mid-change key state can't get stuck.
            _suppressingTarget = false;
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    var wm = wParam.ToInt32();
                    bool isKeyDown = wm == NativeMethods.WM_KEYDOWN || wm == NativeMethods.WM_SYSKEYDOWN;
                    bool isKeyUp = wm == NativeMethods.WM_KEYUP || wm == NativeMethods.WM_SYSKEYUP;

                    if (isKeyDown || isKeyUp)
                    {
                        var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);

                        // Ignore events that we (or any app) injected via SendInput. Processing
                        // our own synthetic keystrokes would let injected input re-trigger the
                        // hotkey and corrupt modifier state.
                        if ((kb.flags & NativeMethods.LLKHF_INJECTED) != 0)
                        {
                            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                        }

                        if (kb.vkCode == _targetVk)
                        {
                            if (isKeyDown)
                            {
                                // Engage only when the full hotkey combo is held. A plain
                                // target-key press (e.g., Alt without Ctrl) is left alone so
                                // the focused app's own Alt shortcuts keep working. While
                                // suspended (Settings open) we ignore activations entirely.
                                if (!IsSuspended && ModifiersMatch() && !_suppressingTarget)
                                {
                                    _suppressingTarget = true;
                                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                                }
                            }
                            else if (isKeyUp)
                            {
                                if (_suppressingTarget)
                                {
                                    _suppressingTarget = false;
                                    HotkeyReleased?.Invoke(this, EventArgs.Empty);
                                }
                            }

                            // NOTE: We intentionally do NOT suppress the target key here.
                            // Swallowing the physical Alt key-up freezes the OS async key
                            // state as "Alt down", which then turns our injected typing into
                            // Alt+char. Letting the real up through keeps the key state
                            // accurate. Menu activation is avoided because the hotkey is a
                            // chord (Ctrl+Alt), not a lone Alt tap.
                        }
                    }
                }
            }
            catch
            {
                // swallow to avoid breaking the hook chain
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private bool ModifiersMatch()
        {
            // configured modifiers e.g. "Ctrl" or "Ctrl+Shift"
            var raw = ConfiguredModifiers ?? string.Empty;

            // Determine which modifiers the configured combo requires.
            bool wantCtrl = false, wantAlt = false, wantShift = false, wantWin = false;
            var parts = raw.Split(new[] { '+', ',', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                switch (p.Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        wantCtrl = true;
                        break;
                    case "alt":
                        wantAlt = true;
                        break;
                    case "shift":
                        wantShift = true;
                        break;
                    case "win":
                    case "lwin":
                    case "rwin":
                        wantWin = true;
                        break;
                }
            }

            // Read the current physical state of each modifier.
            bool ctrlDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
            bool altDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
            bool shiftDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
            bool winDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LWIN) & 0x8000) != 0
                || (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RWIN) & 0x8000) != 0;

            // Exact match: every required modifier must be down and every non-required modifier
            // must be up. This keeps combos that share a target key (e.g. Ctrl+Space vs
            // Ctrl+Shift+Space) from cross-triggering each other.
            return ctrlDown == wantCtrl
                && altDown == wantAlt
                && shiftDown == wantShift
                && winDown == wantWin;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            Stop();
            _isDisposed = true;
        }

        private static class NativeMethods
        {
            public const int WH_KEYBOARD_LL = 13;
            public const int WM_KEYDOWN = 0x0100;
            public const int WM_KEYUP = 0x0101;
            public const int WM_SYSKEYDOWN = 0x0104;
            public const int WM_SYSKEYUP = 0x0105;

            // KBDLLHOOKSTRUCT.flags bit set when the event was injected via SendInput.
            public const uint LLKHF_INJECTED = 0x00000010;

            public const int VK_SHIFT = 0x10;
            public const int VK_CONTROL = 0x11;
            public const int VK_MENU = 0x12; // Alt
            public const int VK_LWIN = 0x5B;
            public const int VK_RWIN = 0x5C;

            public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

            [StructLayout(LayoutKind.Sequential)]
            public struct KBDLLHOOKSTRUCT
            {
                public uint vkCode;
                public uint scanCode;
                public uint flags;
                public uint time;
                public UIntPtr dwExtraInfo;
            }

            [DllImport("user32.dll", SetLastError = true)]
            public static extern short GetAsyncKeyState(int vKey);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern IntPtr GetModuleHandle(string lpModuleName);

            public static IntPtr SetHook(LowLevelKeyboardProc proc)
            {
                using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                var moduleHandle = GetModuleHandle(curModule?.ModuleName ?? "");
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
            }
        }
    }
}
