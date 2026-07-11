using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using VoiceType.Infrastructure.Input;
using VoiceType.Infrastructure.Logging;

namespace VoiceType.Infrastructure.Input
{
    public class InputInjectionService
    {
        // Text insertion method toggles. These are intended to be configurable later
        // through a settings window; exactly one should be active at a time. Clipboard
        // paste is enabled by default and character typing is the alternative.
        public bool UseClipboardPaste { get; set; } = true;
        public bool UseCharacterTyping { get; set; } = false;

        // Whether to restore the user's previous clipboard content after pasting. Disabled
        // by default: async paste targets (WebView2/Electron) read the clipboard after our
        // restore runs and would paste the restored (old) content instead of the transcript.
        public bool RestoreClipboard { get; set; } = false;

        // Delay (ms) before sending Ctrl+V, to allow keyboard focus to settle on the
        // target control after the previous window is restored to the foreground.
        public int PrePasteDelayMs { get; set; } = 150;

        // Delay (ms) after placing text on the clipboard before sending Ctrl+V, so the
        // clipboard has settled.
        public int ClipboardSetDelayMs { get; set; } = 20;

        // Delay (ms) after Ctrl+V before restoring the clipboard. Only matters when
        // RestoreClipboard is enabled: async inputs (WebView2/Electron) read the clipboard
        // asynchronously, so restoring too early can wipe the text before it lands. Restore
        // is disabled by default, so this is 0 (no wait) to keep paste snappy.
        public int PasteCompletionDelayMs { get; set; } = 0;

        // Inserts text into the currently focused application using the configured method.
        // Clipboard paste (Ctrl+V) is fast and preserves formatting; character typing
        // (SendInput) works in fields that block paste.
        public async Task<bool> InsertTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            if (UseClipboardPaste)
            {
                return await PasteViaClipboardAsync(text);
            }

            if (UseCharacterTyping)
            {
                return await TypeViaSendInputAsync(text);
            }

            // Neither method explicitly enabled: use the safe default (clipboard paste).
            return await PasteViaClipboardAsync(text);
        }

        // Clipboard paste (Ctrl+V) with character typing as a safety fallback.
        private async Task<bool> PasteViaClipboardAsync(string text)
        {
            var backup = RestoreClipboard ? await ClipboardHelper.BackupClipboardAsync() : null;
            try
            {
                var didSet = await ClipboardHelper.SetTextAsync(text);
                Logger.Info($"PasteViaClipboard: set clipboard (didSet={didSet}, len={text.Length}): '{Preview(text)}'");
                if (ClipboardSetDelayMs > 0)
                    await Task.Delay(ClipboardSetDelayMs);

                // Confirm OUR text actually reached the clipboard before pasting. If the set
                // failed (another app held the clipboard lock) the clipboard still holds the
                // previous content, and a Ctrl+V would paste that stale text. In that case,
                // fall back to typing so the user always gets the spoken text.
                var readBack = await ClipboardHelper.GetTextAsync();
                var clipboardHasOurText = string.Equals(readBack, text, StringComparison.Ordinal);
                Logger.Info($"PasteViaClipboard: readback before paste (match={clipboardHasOurText}, len={readBack?.Length ?? 0}): '{Preview(readBack)}'");
                if (!didSet || !clipboardHasOurText)
                {
                    Logger.Error($"PasteViaClipboard: clipboard did not contain the transcript (didSet={didSet}). Falling back to typing.");
                    return await TypeViaSendInputAsync(text);
                }

                // Allow keyboard focus to fully settle on the target control before pasting.
                if (PrePasteDelayMs > 0)
                    await Task.Delay(PrePasteDelayMs);

                await SendCtrlVAsync();

                // Wait for the target to consume the paste before we touch the clipboard
                // again. Async inputs (WebView2/Electron) read the clipboard asynchronously,
                // so restoring too early wipes the text before it lands and the target then
                // reads the RESTORED (old) content instead of the transcript.
                if (PasteCompletionDelayMs > 0)
                    await Task.Delay(PasteCompletionDelayMs);

                return true;
            }
            catch
            {
                // fallback: type via SendInput
                return await TypeViaSendInputAsync(text);
            }
            finally
            {
                if (backup != null)
                {
                    Logger.Info($"PasteViaClipboard: restoring previous clipboard (len={backup.Text?.Length ?? 0}): '{Preview(backup.Text)}'");
                    try { await ClipboardHelper.RestoreAsync(backup); } catch { }
                }
            }
        }

        // Short, single-line, truncated preview of clipboard text for diagnostic logging.
        private static string Preview(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return "<empty>";
            var t = s.Length > 40 ? s.Substring(0, 40) + "..." : s;
            return t.Replace("\r", " ").Replace("\n", " ");
        }

        // Character-by-character typing via SendInput.
        private async Task<bool> TypeViaSendInputAsync(string text)
        {
            try
            {
                // The activation hotkey may still be logically held, so target apps could
                // treat typed characters as menu accelerators or control chords. Wait for the
                // modifiers to release first.
                await WaitForModifiersReleasedAsync();

                // Allow keyboard focus to fully settle on the target control before typing.
                if (PrePasteDelayMs > 0)
                    await Task.Delay(PrePasteDelayMs);

                SendTextViaSendInput(text);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"TypeViaSendInput failed: {ex}");
                return false;
            }
        }

        private async Task SendCtrlVAsync()
        {
            // The activation hotkey may still be logically held. Wait for the modifiers to
            // actually release before pasting, otherwise our synthetic Ctrl+V is combined
            // with the held modifier (e.g. Ctrl+Alt+V) and does nothing.
            await WaitForModifiersReleasedAsync();

            // Send the whole Ctrl+V chord in one atomic SendInput call.
            var injected = Native.SendCtrlV(out var err, out var expected);
            if (injected < expected)
            {
                Logger.Error($"SendCtrlV: paste BLOCKED - injected {injected}/{expected} events, firstWin32Error={err} " +
                             $"({(err == 5 ? "ACCESS_DENIED - target likely runs at a higher integrity level than VoiceType" : "see winerror.h")}).");
            }
        }

        // Waits for the user to physically release modifier keys (Alt/Ctrl/Shift/Win) that
        // were part of the activation hotkey, then injects a release for any that remain, so
        // synthetic typing/paste is never combined with a held modifier. Returns once the
        // keys report up or the timeout elapses.
        private async Task WaitForModifiersReleasedAsync(int timeoutMs = 1500, int pollMs = 25)
        {
            var start = Environment.TickCount;
            while (AnyModifierDown())
            {
                if (Environment.TickCount - start >= timeoutMs)
                    break;
                await Task.Delay(pollMs);
            }

            // Belt-and-suspenders: inject key-up for anything still logically held.
            ReleaseModifierKeys();
        }

        private static bool AnyModifierDown()
        {
            bool down(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
            return down(Native.VK_MENU) || down(Native.VK_CONTROL) || down(Native.VK_SHIFT)
                || down(Native.VK_LWIN) || down(Native.VK_RWIN);
        }

        // Sends key-up for every modifier that could still be logically held from the
        // activation hotkey, ensuring a synthetic Ctrl+V is not combined with Alt/Shift/Win.
        private void ReleaseModifierKeys()
        {
            Native.SendInputKeyboard((ushort)Native.VK_LMENU, true, true);   // Left Alt up
            Native.SendInputKeyboard((ushort)Native.VK_RMENU, true, true);   // Right Alt up
            Native.SendInputKeyboard((ushort)Native.VK_MENU, true, true);    // Alt up
            Native.SendInputKeyboard((ushort)Native.VK_LCONTROL, true, true);
            Native.SendInputKeyboard((ushort)Native.VK_RCONTROL, true, true);
            Native.SendInputKeyboard((ushort)Native.VK_CONTROL, true, true);
            Native.SendInputKeyboard((ushort)Native.VK_SHIFT, true, true);
            Native.SendInputKeyboard((ushort)Native.VK_LWIN, true, true);
            Native.SendInputKeyboard((ushort)Native.VK_RWIN, true, true);
        }

        private void SendTextViaSendInput(string text)
        {
            // Inject the ENTIRE string in a single SendInput call. Sending one character per
            // call (a rapid burst of separate calls) lets legacy Win32 Edit controls such as
            // Notepad drop and reorder the WM_CHAR messages, producing garbled output like
            // "Hello iceType!". A single call delivers all key events serially and atomically.
            var injected = Native.SendUnicodeText(text, out var err, out var expected);

            // SendInput returns the number of events actually inserted. Zero means the input
            // was blocked (commonly UIPI / integrity-level mismatch: error 5 = ACCESS_DENIED).
            if (injected < expected)
            {
                Logger.Error($"SendTextViaSendInput: injection BLOCKED - injected {injected}/{expected} events, firstWin32Error={err} " +
                             $"({(err == 5 ? "ACCESS_DENIED - target likely runs at a higher integrity level than VoiceType" : "see winerror.h")}).");
            }
        }

        private static class Native
        {
            public const int INPUT_KEYBOARD = 1;
            public const uint KEYEVENTF_KEYUP = 0x0002;
            public const ushort VK_CONTROL = 0x11;
            public const ushort VK_V = 0x56;
            public const ushort VK_MENU = 0x12;    // Alt
            public const ushort VK_LMENU = 0xA4;   // Left Alt
            public const ushort VK_RMENU = 0xA5;   // Right Alt
            public const ushort VK_LCONTROL = 0xA2;
            public const ushort VK_RCONTROL = 0xA3;
            public const ushort VK_SHIFT = 0x10;
            public const ushort VK_LWIN = 0x5B;
            public const ushort VK_RWIN = 0x5C;

            [StructLayout(LayoutKind.Sequential)]
            public struct INPUT
            {
                public int type;
                public InputUnion u;
            }

            // The union must be sized to its LARGEST member (MOUSEINPUT). Defining it with
            // only KEYBDINPUT makes the struct 24 bytes on x64 instead of the required 40,
            // so SendInput rejects every call with ERROR_INVALID_PARAMETER (87) and injects
            // nothing. Including all three members forces the correct native size/layout.
            [StructLayout(LayoutKind.Explicit)]
            public struct InputUnion
            {
                [FieldOffset(0)] public MOUSEINPUT mi;
                [FieldOffset(0)] public KEYBDINPUT ki;
                [FieldOffset(0)] public HARDWAREINPUT hi;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MOUSEINPUT
            {
                public int dx;
                public int dy;
                public uint mouseData;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct KEYBDINPUT
            {
                public ushort wVk;
                public ushort wScan;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct HARDWAREINPUT
            {
                public uint uMsg;
                public ushort wParamL;
                public ushort wParamH;
            }

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

            [DllImport("user32.dll")]
            public static extern short GetAsyncKeyState(int vKey);

            public static void SendInputKeyboard(ushort vk, bool keyUp, bool useVk)
            {
                var input = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = useVk ? vk : (ushort)0,
                            wScan = useVk ? (ushort)0 : vk,
                            dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                var arr = new[] { input };
                SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
            }

            // Sends Ctrl+V as a single atomic SendInput call (Ctrl down, V down, V up, Ctrl up).
            // One call keeps the four events strictly ordered and avoids interleaving with any
            // other input, which is more reliable than four separate calls. Returns the number
            // of events actually injected so callers can detect a blocked paste.
            public static uint SendCtrlV(out int lastError, out int expected)
            {
                INPUT Key(ushort vk, bool keyUp) => new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = vk,
                            wScan = 0,
                            dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };

                var inputs = new[]
                {
                    Key(VK_CONTROL, false),
                    Key(VK_V, false),
                    Key(VK_V, true),
                    Key(VK_CONTROL, true)
                };

                expected = inputs.Length;
                var injected = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                lastError = injected == inputs.Length ? 0 : Marshal.GetLastWin32Error();
                return injected;
            }

            // Injects an entire string as Unicode key events in a SINGLE SendInput call.
            // Each UTF-16 code unit becomes a keydown+keyup pair with KEYEVENTF_UNICODE, and
            // surrogate pairs are emitted as consecutive units so emoji/non-BMP text works.
            // Returns the number of events SendInput actually injected; 'expected' is the
            // number requested and 'lastError' is the Win32 error when injected < expected.
            public static uint SendUnicodeText(string text, out int lastError, out int expected)
            {
                const uint KEYEVENTF_UNICODE = 0x0004;

                var inputs = new INPUT[text.Length * 2];
                int idx = 0;
                foreach (var ch in text)
                {
                    var down = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = ch,
                                dwFlags = KEYEVENTF_UNICODE,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero
                            }
                        }
                    };
                    var up = down;
                    up.u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;

                    inputs[idx++] = down;
                    inputs[idx++] = up;
                }

                expected = inputs.Length;
                var injected = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                lastError = injected == inputs.Length ? 0 : Marshal.GetLastWin32Error();
                return injected;
            }
        }
    }
}
