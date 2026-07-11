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
        private readonly Func<IReadOnlyList<string>> _getModels;
        private readonly Func<string?> _getActiveModel;
        private readonly Func<string, Task> _onModelSelected;
        private Icon? _ownedIcon;
        private bool _disposed;

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
            Func<string, Task> onModelSelected)
        {
            ArgumentNullException.ThrowIfNull(onOpenSettings);
            ArgumentNullException.ThrowIfNull(onExit);
            ArgumentNullException.ThrowIfNull(getModels);
            ArgumentNullException.ThrowIfNull(getActiveModel);
            ArgumentNullException.ThrowIfNull(onModelSelected);

            _getModels = getModels;
            _getActiveModel = getActiveModel;
            _onModelSelected = onModelSelected;

            _menu = new ContextMenuStrip();

            // The Model submenu is rebuilt each time it opens so it reflects newly added model
            // files and the currently active model without restarting the app.
            _modelMenu = new ToolStripMenuItem("Model");
            _modelMenu.DropDownOpening += (_, _) => RebuildModelMenu();

            var openSettingsItem = new ToolStripMenuItem("Open Settings");
            openSettingsItem.Click += (_, _) => SafeInvoke(onOpenSettings, "Open Settings");

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => SafeInvoke(onExit, "Exit");

            _menu.Items.Add(_modelMenu);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(openSettingsItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);

            _notifyIcon = new NotifyIcon
            {
                Text = "VoiceType",
                Icon = ResolveApplicationIcon(),
                ContextMenuStrip = _menu,
                Visible = true
            };

            // Double-clicking the tray icon opens settings, matching the primary menu action.
            _notifyIcon.DoubleClick += (_, _) => SafeInvoke(onOpenSettings, "Open Settings (double-click)");
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

        // Resolves the running executable's own icon. Falls back to the default application
        // icon if extraction fails so the tray icon is never blank.
        private Icon ResolveApplicationIcon()
        {
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

            // Hide before disposing so the icon never lingers in the tray until the shell repaints.
            try { _notifyIcon.Visible = false; } catch { }
            try { _notifyIcon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            try { _ownedIcon?.Dispose(); } catch { }
        }
    }
}
