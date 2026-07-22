using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using VoiceType.Infrastructure.Logging;

namespace VoiceType.UI
{
    /// <summary>
    /// Which input mode started the active dictation session, used to give the tray icon a
    /// distinct look so the user can tell hold-to-talk (hotkey) apart from hands-free (toggle).
    /// </summary>
    public enum ListeningMode
    {
        /// <summary>No active session; show the idle icon.</summary>
        None,
        /// <summary>Started by holding the global hotkey (push/hold-to-talk).</summary>
        Hotkey,
        /// <summary>Started by a tray single-click (hands-free toggle).</summary>
        Toggle
    }

    /// <summary>
    /// Hosts the WinForms <see cref="NotifyIcon"/> that acts as VoiceType's sole control
    /// center. The app is windowless at startup (ShutdownMode=OnExplicitShutdown), so this
    /// tray icon and its context menu (Model, Open Settings, Exit) are the primary user entry
    /// points. The NotifyIcon is a WinForms object hosted inside the WPF app; WPF's Dispatcher
    /// provides the message loop, so no separate Application.Run is needed.
    /// </summary>
    public sealed class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _modelMenu;
        private readonly ToolStripMenuItem _toggleModeItem;
        private readonly Func<IReadOnlyList<string>> _getModels;
        private readonly Func<string?> _getActiveModel;
        private readonly Func<string, Task> _onModelSelected;
        private readonly Action _onOpenSettings;
        private readonly Action _onToggleDictation;
        private readonly Action<bool> _onToggleModeChanged;
        private readonly Action? _onViewHistory;
        private readonly ToolStripMenuItem _viewHistoryItem;
        private Icon? _ownedIcon;
        private Icon? _recordingIconToggle;
        private Icon? _recordingIconHotkey;
        private Icon? _baseIcon;
        private bool _isListening;
        private System.Windows.Threading.DispatcherTimer? _singleClickTimer;
        private DateTime _suppressClickUntilUtc;
        private bool _disposed;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// Creates and shows the tray icon.
        /// </summary>
        /// <param name="onOpenSettings">Invoked when the user selects "Open Settings".</param>
        /// <param name="onExit">Invoked when the user selects "Exit".</param>
        /// <param name="getModels">Returns the available model file paths (ggml-*.bin).</param>
        /// <param name="getActiveModel">Returns the currently active model file path, if any.</param>
        /// <param name="onModelSelected">Invoked with the chosen model file path when the user picks a model.</param>
        public TrayIconManager(
            Action onOpenSettings,
            Action onExit,
            Func<IReadOnlyList<string>> getModels,
            Func<string?> getActiveModel,
            Func<string, Task> onModelSelected,
            Action onToggleDictation,
            Action<bool> onToggleModeChanged,
            bool toggleModeEnabled,
            Action? onViewHistory = null,
            bool historyEnabled = false)
        {
            ArgumentNullException.ThrowIfNull(onOpenSettings);
            ArgumentNullException.ThrowIfNull(onExit);
            ArgumentNullException.ThrowIfNull(getModels);
            ArgumentNullException.ThrowIfNull(getActiveModel);
            ArgumentNullException.ThrowIfNull(onModelSelected);
            ArgumentNullException.ThrowIfNull(onToggleDictation);
            ArgumentNullException.ThrowIfNull(onToggleModeChanged);
            _onViewHistory = onViewHistory;

            _getModels = getModels;
            _getActiveModel = getActiveModel;
            _onModelSelected = onModelSelected;
            _onOpenSettings = onOpenSettings;
            _onToggleDictation = onToggleDictation;
            _onToggleModeChanged = onToggleModeChanged;

            _menu = new ContextMenuStrip();

            // The Model submenu is rebuilt each time it opens so it reflects newly added model
            // files and the currently active model without restarting the app.
            _modelMenu = new ToolStripMenuItem("Model");
            _modelMenu.DropDownOpening += (_, _) => RebuildModelMenu();

            // Checkable item lets the user enable/disable single-click tray toggle mode. Kept in
            // sync with the Settings window via _onToggleModeChanged / SetToggleModeEnabled.
            _toggleModeItem = new ToolStripMenuItem("Toggle mode (single-click)")
            {
                CheckOnClick = true,
                Checked = toggleModeEnabled
            };
            _toggleModeItem.CheckedChanged += (_, _) =>
            {
                if (!_isListening && !_disposed)
                    _notifyIcon.Text = IdleTooltip();
                SafeInvoke(() => _onToggleModeChanged(_toggleModeItem.Checked), "Toggle mode changed");
            };

            var openSettingsItem = new ToolStripMenuItem("Open Settings");
            openSettingsItem.Click += (_, _) => SafeInvoke(onOpenSettings, "Open Settings");

            _viewHistoryItem = new ToolStripMenuItem("View Transcript History")
            {
                Visible = _onViewHistory != null && historyEnabled
            };
            _viewHistoryItem.Click += (_, _) => SafeInvoke(() => _onViewHistory?.Invoke(), "View Transcript History");

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => SafeInvoke(onExit, "Exit");

            _menu.Items.Add(_modelMenu);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_toggleModeItem);
            _menu.Items.Add(openSettingsItem);
            if (_onViewHistory != null)
                _menu.Items.Add(_viewHistoryItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Text = "VoiceType",
                Icon = ResolveApplicationIcon(),
                ContextMenuStrip = _menu,
                Visible = true
            };
            _baseIcon = _notifyIcon.Icon;
            _notifyIcon.Text = IdleTooltip();

            // Left single-click toggles dictation when toggle mode is on. A short timer defers the
            // single-click action so a double-click (open Settings) can cancel it and win instead.
            _notifyIcon.MouseUp += OnTrayMouseUp;
            _notifyIcon.DoubleClick += OnTrayDoubleClick;
        }

        // Single vs double click disambiguation: MouseUp starts a timer; if a DoubleClick arrives
        // first it cancels the timer and opens Settings. A double-click also raises MouseUp twice,
        // so we suppress clicks briefly after a double-click to stop the toggle from firing too.
        private void OnTrayMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (!_toggleModeItem.Checked) return;
            if (DateTime.UtcNow < _suppressClickUntilUtc) return;

            _singleClickTimer?.Stop();
            _singleClickTimer ??= CreateSingleClickTimer();
            _singleClickTimer.Start();
        }

        private void OnTrayDoubleClick(object? sender, EventArgs e)
        {
            _singleClickTimer?.Stop();
            // Ignore the trailing MouseUp(s) of this double-click so the single-click toggle
            // doesn't also fire (a double-click delivers MouseUp both before and after DoubleClick).
            _suppressClickUntilUtc = DateTime.UtcNow.AddMilliseconds(SystemInformation.DoubleClickTime + 100);
            SafeInvoke(_onOpenSettings, "Open Settings (double-click)");
        }

        private System.Windows.Threading.DispatcherTimer CreateSingleClickTimer()
        {
            var interval = TimeSpan.FromMilliseconds(SystemInformation.DoubleClickTime + 50);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = interval };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                SafeInvoke(_onToggleDictation, "Toggle dictation (single-click)");
            };
            return timer;
        }

        /// <summary>
        /// Reflects the current dictation state on the tray icon. When listening it swaps to a
        /// mode-specific recording icon (green dot = hands-free toggle, red dot = hold-to-talk
        /// hotkey) and updates the tooltip so the user can tell the two modes apart.
        /// Safe to call from any thread.
        /// </summary>
        public void SetListeningState(ListeningMode mode)
        {
            if (_disposed) return;

            _isListening = mode != ListeningMode.None;

            void Apply()
            {
                try
                {
                    _notifyIcon.Icon = mode switch
                    {
                        ListeningMode.Toggle => GetRecordingIcon(ListeningMode.Toggle) ?? _notifyIcon.Icon,
                        ListeningMode.Hotkey => GetRecordingIcon(ListeningMode.Hotkey) ?? _notifyIcon.Icon,
                        _ => _baseIcon ?? _notifyIcon.Icon
                    };
                    _notifyIcon.Text = mode switch
                    {
                        ListeningMode.Toggle => "VoiceType - listening (hands-free)",
                        ListeningMode.Hotkey => "VoiceType - listening (hold-to-talk)",
                        _ => IdleTooltip()
                    };
                }
                catch (Exception ex)
                {
                    Logger.Error($"TrayIconManager: failed to set listening icon: {ex.Message}");
                }
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }

        /// <summary>
        /// Builds the idle-state tooltip, reflecting how the user starts dictation (single-click
        /// hands-free toggle vs holding the global hotkey).
        /// </summary>
        private string IdleTooltip() => _toggleModeItem.Checked
            ? "VoiceType - idle (single-click to talk)"
            : "VoiceType - idle (hold hotkey to talk)";

        /// <summary>
        /// Updates the checkable toggle-mode menu item to match the persisted setting (e.g. after
        /// the user changes it in the Settings window). Safe to call from any thread.
        /// </summary>
        public void SetToggleModeEnabled(bool enabled)
        {
            if (_disposed) return;

            void Apply()
            {
                _toggleModeItem.Checked = enabled;
                // Keep the idle tooltip in step with the current input mode.
                if (!_isListening)
                    _notifyIcon.Text = IdleTooltip();
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }

        /// <summary>
        /// Shows or hides the "View Transcript History" menu item to match the persisted
        /// EnableTranscriptHistory setting (e.g. after the user changes it in the Settings window).
        /// No-ops if no history callback was ever supplied. Safe to call from any thread.
        /// </summary>
        public void SetHistoryEnabled(bool enabled)
        {
            if (_disposed || _onViewHistory is null) return;

            void Apply() => _viewHistoryItem.Visible = enabled;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(Apply);
            else
                Apply();
        }

        // Rebuilds the checkable Model submenu from the current models folder, marking the active
        // model. Shows a disabled placeholder when no models are found.
        private void RebuildModelMenu()
        {
            _modelMenu.DropDownItems.Clear();

            IReadOnlyList<string> models;
            string? active;
            try
            {
                models = _getModels() ?? Array.Empty<string>();
                active = _getActiveModel();
            }
            catch (Exception ex)
            {
                Logger.Error($"TrayIconManager: failed to enumerate models: {ex.Message}");
                _modelMenu.DropDownItems.Add(new ToolStripMenuItem("(error loading models)") { Enabled = false });
                return;
            }

            if (models.Count == 0)
            {
                _modelMenu.DropDownItems.Add(new ToolStripMenuItem("(no models found)") { Enabled = false });
                return;
            }

            var activeName = string.IsNullOrEmpty(active) ? null : Path.GetFileName(active);

            foreach (var modelPath in models)
            {
                var item = new ToolStripMenuItem(Path.GetFileNameWithoutExtension(modelPath))
                {
                    Tag = modelPath,
                    Checked = activeName is not null &&
                              string.Equals(Path.GetFileName(modelPath), activeName, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += OnModelItemClick;
                _modelMenu.DropDownItems.Add(item);
            }
        }

        private async void OnModelItemClick(object? sender, EventArgs e)
        {
            if (sender is not ToolStripMenuItem item || item.Tag is not string modelPath)
                return;

            // Already the active model: nothing to do.
            if (item.Checked) return;

            try
            {
                await _onModelSelected(modelPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error($"TrayIconManager: model switch to '{modelPath}' failed: {ex}");
                ShowBalloon("VoiceType", $"Failed to switch model: {ex.Message}", ToolTipIcon.Error);
            }
        }

        /// <summary>
        /// Shows a balloon tip from the tray icon. Used to surface fatal startup/runtime errors
        /// since the app has no main window.
        /// </summary>
        public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 5000)
        {
            if (_disposed) return;

            try
            {
                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = text;
                _notifyIcon.BalloonTipIcon = icon;
                _notifyIcon.ShowBalloonTip(timeoutMs);
            }
            catch (Exception ex)
            {
                Logger.Error($"TrayIconManager: failed to show balloon: {ex.Message}");
            }
        }

        // Resolves the tray icon. Prefers the packaged multi-resolution voicetype.ico so the
        // shell can pick the sharpest frame for the current DPI/tray size. Falls back to the
        // running executable's own icon, then the default application icon, so the tray icon is
        // never blank.
        private Icon ResolveApplicationIcon()
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var icoPath = Path.Combine(baseDir, "Assets", "voicetype.ico");
                if (File.Exists(icoPath))
                {
                    // Load the frame that best matches the current small-icon metric.
                    var desired = SystemInformation.SmallIconSize;
                    _ownedIcon = new Icon(icoPath, desired);
                    return _ownedIcon;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"TrayIconManager: could not load voicetype.ico: {ex.Message}");
            }

            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var extracted = Icon.ExtractAssociatedIcon(exePath);
                    if (extracted is not null)
                    {
                        _ownedIcon = extracted;
                        return extracted;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"TrayIconManager: could not extract exe icon: {ex.Message}");
            }

            return SystemIcons.Application;
        }

        // Builds (once per mode) a "recording" variant of the tray icon by drawing a small colored
        // dot in the lower-right corner of the base icon. Green = hands-free toggle, red = hold-to-
        // talk hotkey, so the user can tell which mode started the active session.
        private Icon? GetRecordingIcon(ListeningMode mode)
        {
            ref Icon? cache = ref (mode == ListeningMode.Toggle ? ref _recordingIconToggle : ref _recordingIconHotkey);
            if (cache is not null) return cache;

            var dotColor = mode == ListeningMode.Toggle ? Color.LimeGreen : Color.Red;

            try
            {
                var size = SystemInformation.SmallIconSize;
                using var bmp = new Bitmap(size.Width, size.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    if (_baseIcon is not null)
                        g.DrawIcon(_baseIcon, new Rectangle(0, 0, size.Width, size.Height));

                    var d = Math.Max(4, size.Width / 3);
                    var rect = new Rectangle(size.Width - d, size.Height - d, d, d);
                    using var brush = new SolidBrush(dotColor);
                    using var pen = new Pen(Color.White, Math.Max(1f, size.Width / 16f));
                    g.FillEllipse(brush, rect);
                    g.DrawEllipse(pen, rect);
                }

                var hIcon = bmp.GetHicon();
                using var tmp = Icon.FromHandle(hIcon);
                cache = (Icon)tmp.Clone();
                DestroyIcon(hIcon);
                return cache;
            }
            catch (Exception ex)
            {
                Logger.Error($"TrayIconManager: failed to build recording icon: {ex.Message}");
                return null;
            }
        }

        private static void SafeInvoke(Action action, string label)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Logger.Error($"TrayIconManager: '{label}' handler failed: {ex}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _singleClickTimer?.Stop(); } catch { }

            // Hide before disposing so the icon never lingers in the tray until the shell repaints.
            try { _notifyIcon.Visible = false; } catch { }
            try { _notifyIcon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            try { _ownedIcon?.Dispose(); } catch { }
            try { _recordingIconToggle?.Dispose(); } catch { }
            try { _recordingIconHotkey?.Dispose(); } catch { }
        }
    }
}
