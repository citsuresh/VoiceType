using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using VoiceType.Infrastructure.Logging;

namespace VoiceType.Infrastructure.Windowing
{
    /// <summary>
    /// Determines whether the control that currently has keyboard focus can accept typed/pasted
    /// text. Uses a fast Win32 class-name pre-filter to short-circuit the common cases, then falls
    /// back to UI Automation (which handles Chromium/Electron/WebView editors that report generic
    /// window classes).
    /// </summary>
    public static class FocusedControlInspector
    {
        /// <summary>
        /// Returns <c>true</c> when the focused control can accept typed/pasted text.
        /// </summary>
        /// <remarks>
        /// This is deliberately biased toward returning <c>true</c>: it only returns <c>false</c>
        /// when we can positively confirm the focused element is a non-editable control. Any
        /// uncertainty (UI Automation unavailable, generic host window, exceptions) is treated as
        /// editable so that the normal paste/type path — which worked reliably before this check
        /// existed — is preserved. The clipboard fallback is reserved for the clear case where
        /// there is genuinely nowhere to type (e.g. focus landed on a button, list, or the shell).
        /// </remarks>
        public static bool IsEditableControlFocused()
        {
            try
            {
                var focused = GetFocusedWindow();
                if (focused == IntPtr.Zero)
                {
                    // No focus HWND at all: could not confirm non-editable, so assume editable.
                    Logger.Info("FocusedControlInspector: no focused window found; assuming editable.");
                    return true;
                }

                // Fast path: well-known native edit/text classes are always editable.
                var className = GetClassName(focused);
                if (IsKnownEditableClass(className))
                {
                    return true;
                }

                // Slow path: UI Automation on the globally focused element (resolves the real
                // editable element inside Chromium/WPF trees, which the focused HWND hides).
                return IsEditableViaAutomation(focused);
            }
            catch (Exception ex)
            {
                // On any failure, keep the previously-working behaviour and paste.
                Logger.Error($"FocusedControlInspector: editable detection failed; assuming editable. {ex}");
                return true;
            }
        }

        private static IntPtr GetFocusedWindow()
        {
            var foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return IntPtr.Zero;

            var threadId = Native.GetWindowThreadProcessId(foreground, IntPtr.Zero);
            if (threadId == 0) return IntPtr.Zero;

            var gui = new Native.GUITHREADINFO();
            gui.cbSize = Marshal.SizeOf<Native.GUITHREADINFO>();
            if (Native.GetGUIThreadInfo(threadId, ref gui) && gui.hwndFocus != IntPtr.Zero)
            {
                return gui.hwndFocus;
            }

            return foreground;
        }

        private static bool IsKnownEditableClass(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;

            // Common native and framework edit control classes.
            if (className is "Edit" or "RICHEDIT" or "RICHEDIT20A" or "RICHEDIT20W"
                or "RICHEDIT50W" or "RichEdit20WPT" or "TextBox" or "Scintilla")
            {
                return true;
            }

            // WPF/WinForms controls expose class names prefixed with the framework namespace.
            var lower = className.ToLowerInvariant();
            return lower.Contains("edit") || lower.Contains("textbox");
        }

        private static bool IsEditableViaAutomation(IntPtr hwnd)
        {
            try
            {
                // Prefer the globally focused element: for Chromium/Electron/WPF the focused HWND is
                // just a host/render window, while FocusedElement resolves the real inner control.
                AutomationElement? element = null;
                try { element = AutomationElement.FocusedElement; } catch { /* fall back below */ }
                element ??= AutomationElement.FromHandle(hwnd);
                if (element is null)
                {
                    // Could not inspect: don't block the paste path.
                    return true;
                }

                // Positive signal: editable Value pattern.
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObj)
                    && valueObj is ValuePattern valuePattern)
                {
                    if (!valuePattern.Current.IsReadOnly)
                    {
                        return true;
                    }
                }

                // Positive signal: text-editing control types.
                var controlType = element.Current.ControlType;
                if (controlType == ControlType.Edit || controlType == ControlType.Document)
                {
                    return true;
                }

                // Supports the Text pattern (many rich editors / web contenteditable regions).
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out _))
                {
                    return true;
                }

                // Definitively non-editable control types: safe to divert to the clipboard.
                if (controlType == ControlType.Button
                    || controlType == ControlType.CheckBox
                    || controlType == ControlType.RadioButton
                    || controlType == ControlType.List
                    || controlType == ControlType.ListItem
                    || controlType == ControlType.Tree
                    || controlType == ControlType.TreeItem
                    || controlType == ControlType.Menu
                    || controlType == ControlType.MenuItem
                    || controlType == ControlType.TabItem
                    || controlType == ControlType.Slider
                    || controlType == ControlType.Hyperlink
                    || controlType == ControlType.Image)
                {
                    return false;
                }

                // Unknown/ambiguous control type: preserve the original paste behaviour.
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"FocusedControlInspector: UI Automation probe failed; assuming editable. {ex}");
                return true;
            }
        }

        private static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            var length = Native.GetClassName(hwnd, sb, sb.Capacity);
            return length > 0 ? sb.ToString() : string.Empty;
        }

        private static class Native
        {
            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

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
