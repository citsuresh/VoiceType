using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VoiceType.Core;
using VoiceType.Infrastructure.Config;
using VoiceType.Infrastructure.Hotkeys;
using VoiceType.Infrastructure.Logging;
using VoiceType.UI;

namespace VoiceType
{
    public partial class App : Application
    {
        // Distinct, machine-wide name so a second launch can detect the first instance.
        private const string SingleInstanceMutexName = "Global\\VoiceType.SingleInstance.9F2C1E7A";

        private Mutex? _singleInstanceMutex;
        private TrayIconManager? _trayIcon;

        // Held so the tray Model submenu can enumerate/switch models and persist the choice.
        private VoiceTypeSettings? _settings;
        private DictationSessionController? _controller;
        private Infrastructure.Whisper.WhisperServerClient? _serverClient;
        private GlobalHotkeyManager? _hotkeyManager;
        private GlobalHotkeyManager? _toggleHotkeyManager;

        // Tracks the last real foreground window (excluding our own process and the shell) so
        // tray-toggle dictation can restore the window the user was actually working in, even
        // though clicking the tray icon momentarily moves foreground to the shell/taskbar.
        private Infrastructure.Windowing.ForegroundWindowMonitor? _foregroundMonitor;

        // Which input path started the current session, so the tray icon can show a mode-specific
        // recording indicator. Set when a session starts; reset to None when it returns to Idle.
        private ListeningMode _activeListeningMode = ListeningMode.None;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Logger.Info("App starting up");

            // Single-instance guard: if another instance already owns the mutex, exit silently.
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                Logger.Info("Another instance is already running; exiting silently.");
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            // Install global exception handlers early so failures anywhere are logged and
            // surfaced via the tray (the app has no main window to show errors in).
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

            // Re-verify (and self-heal) the long-lived whisper-server process after the machine
            // wakes from sleep/hibernate - see OnPowerModeChanged for rationale.
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

            var settings = SettingsLoader.Load();
            var controller = new DictationSessionController(settings);
            Resources["DictationController"] = controller;
            Resources["Settings"] = settings;
            _settings = settings;
            _controller = controller;

            // Sweep orphaned session WAVs left behind by a prior crash/force-kill (the normal
            // cleanup in DictationSessionController only runs when finalization completes
            // normally, so an app that dies mid-session leaks its temp WAV otherwise).
            CleanupStaleTempWavFiles(settings);

            // Transcript-comparison history persistence and shared "latest comparison" state for
            // the post-insertion bulb/comparison popup (see docs/NEW_FEATURE_SPECS.md).
            var historyService = new Infrastructure.History.TranscriptHistoryService(maxEntries: settings.TranscriptHistoryRetentionLimit);
            var previewState = new Core.Preview.TranscriptPreviewState();
            controller.HistoryService = historyService;
            controller.PreviewState = previewState;
            Resources["TranscriptHistoryService"] = historyService;
            Resources["TranscriptPreviewState"] = previewState;

            // Start the foreground-window monitor on the UI thread (it needs a message pump) so we
            // always know the user's last real target window for tray-toggle dictation.
            _foregroundMonitor = new Infrastructure.Windowing.ForegroundWindowMonitor();
            _foregroundMonitor.Start();
            Resources["ForegroundMonitor"] = _foregroundMonitor;

            try
            {
                var probe = new Infrastructure.Whisper.WhisperProcessRunner(settings).ProbePaths();
                Logger.Info($"Whisper probe - executable: {probe.ExecutablePath ?? "(not found)"}, model: {probe.ModelPath ?? "(not found)"}");
            }
            catch { }

            // In Server mode, start the long-lived whisper-server process once at startup so
            // the model stays loaded. Startup is off the hotkey path, so its one-time cost is
            // hidden. If it fails to start, the controller transparently falls back to CLI.
            if (settings.Mode == TranscriptionMode.Server)
            {
                var serverClient = new Infrastructure.Whisper.WhisperServerClient(settings);
                Resources["WhisperServerClient"] = serverClient;
                controller.ServerClient = serverClient;
                _serverClient = serverClient;
                _ = serverClient.StartAsync();
            }

            var hotkeyManager = new GlobalHotkeyManager(settings);
            hotkeyManager.HotkeyPressed += async (s, ev) =>
            {
                Logger.Info("Hotkey pressed");
                if (controller.State == DictationState.Idle)
                    _activeListeningMode = ListeningMode.Hotkey;
                // Pass a physical-key check so the controller can confirm the chord is still held
                // right before it starts listening; a fast tap that already released is aborted.
                try { await controller.StartSessionAsync(isHotkeyStillHeld: hotkeyManager.IsHotkeyPhysicallyDown); } catch { }
            };
            hotkeyManager.HotkeyReleased += async (s, ev) =>
            {
                Logger.Info("Hotkey released");
                try { await controller.StopSessionAsync(); } catch { }
            };

            hotkeyManager.Start();
            Resources["HotkeyManager"] = hotkeyManager;
            _hotkeyManager = hotkeyManager;
            // Hold-to-talk can be disabled entirely in Settings; start the hook only when enabled.
            if (!settings.DictationHotkeyEnabled)
                hotkeyManager.Stop();

            // Toggle-mode hotkey: a single tap toggles a hands-free session (same as clicking the
            // tray icon). Ignored while a hold-to-talk session is active so the two paths never fight.
            var toggleHotkeyManager = new GlobalHotkeyManager(settings, HotkeyKind.Toggle);
            toggleHotkeyManager.HotkeyPressed += (s, ev) =>
            {
                Logger.Info("Toggle hotkey pressed");
                if (_activeListeningMode == ListeningMode.Hotkey)
                    return;
                ToggleDictationFromTray();
            };
            toggleHotkeyManager.Start();
            Resources["ToggleHotkeyManager"] = toggleHotkeyManager;
            _toggleHotkeyManager = toggleHotkeyManager;
            // Toggle mode can be disabled entirely in Settings; start the hook only when enabled.
            if (!settings.ToggleModeEnabled)
                toggleHotkeyManager.Stop();

            // Tray icon is the sole control center for this windowless app.
            _trayIcon = new TrayIconManager(
                onOpenSettings: () => SettingsWindow.ShowSingleInstance(),
                onExit: () => Shutdown(),
                getModels: () => new Infrastructure.Whisper.WhisperProcessRunner(settings).EnumerateModels(),
                getActiveModel: () => settings.WhisperModelPath,
                onModelSelected: SwitchModelAsync,
                onToggleDictation: ToggleDictationFromTray,
                onToggleModeChanged: OnTrayToggleModeChanged,
                toggleModeEnabled: settings.UseTrayIconToggle,
                historyEnabled: settings.EnableTranscriptHistory,
                onViewHistory: () =>
                {
                    var existing = UI.ComparisonWindow.GetOpenWindow();
                    if (existing is not null)
                    {
                        existing.Activate();
                        return;
                    }

                    var popup = new UI.ComparisonWindow { ShowActivated = true, HistoryService = historyService };
                    popup.LoadEntries(historyService.GetEntries());
                    popup.Show();
                });

            // Reflect dictation state on the tray icon (mode-specific recording indicator) and
            // drive idle auto-stop only for tray-toggle sessions.
            controller.StateChanged += state =>
            {
                var listening = state == DictationState.Listening || state == DictationState.Previewing;
                if (!listening)
                    _activeListeningMode = ListeningMode.None;

                _trayIcon?.SetListeningState(listening ? _activeListeningMode : ListeningMode.None);
            };
        }

        // Tray single-click handler: toggles a hands-free dictation session. When starting via the
        // tray (not the hold-to-talk hotkey), optionally arm idle auto-stop from settings.
        private void ToggleDictationFromTray()
        {
            var controller = _controller;
            var settings = _settings;
            if (controller is null || settings is null) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    if (controller.State == DictationState.Idle)
                    {
                        // Toggle mode can be disabled entirely in Settings; don't start a
                        // hands-free session from the tray when it's turned off.
                        if (!settings.ToggleModeEnabled)
                            return;

                        _activeListeningMode = ListeningMode.Toggle;

                        // Clicking the tray icon moves foreground to the shell, so pass the last
                        // real foreground window (and its focused control) captured by the monitor.
                        var preferredHwnd = _foregroundMonitor?.LastForegroundHandle ?? IntPtr.Zero;
                        var preferredFocus = _foregroundMonitor?.LastFocusHandle ?? IntPtr.Zero;

                        if (settings.ToggleIdleAutoStopEnabled)
                            controller.ArmIdleAutoStop(settings.ToggleIdleAutoStopSeconds);

                        await controller.StartSessionAsync(preferredForegroundHwnd: preferredHwnd, preferredFocusHwnd: preferredFocus).ConfigureAwait(false);
                    }
                    else
                    {
                        await controller.StopSessionAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"App: tray toggle dictation failed: {ex}");
                }
            });
        }

        // Persists the tray context-menu "Toggle mode" checkbox change to settings.
        private void OnTrayToggleModeChanged(bool enabled)
        {
            var settings = _settings;
            if (settings is null || settings.UseTrayIconToggle == enabled) return;

            settings.UseTrayIconToggle = enabled;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try { await SettingsLoader.SaveAsync(settings).ConfigureAwait(false); }
                catch (Exception ex) { Logger.Error($"App: failed to persist tray toggle mode: {ex}"); }
            });
        }

        // Applies a runtime model switch: mutates the shared settings, persists to appsettings.json,
        // restarts the whisper-server (Server mode) so the new model is loaded, and refreshes the
        // overlay model bubble. CLI/Stream modes simply pick up the new model on the next session.
        private async Task SwitchModelAsync(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || _settings is null)
                return;

            Logger.Info($"App: switching model to '{modelPath}'.");
            _settings.WhisperModelPath = modelPath;

            try
            {
                await SettingsLoader.SaveAsync(_settings).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error($"App: failed to persist model selection: {ex}");
                _trayIcon?.ShowBalloon("VoiceType", $"Could not save model selection: {ex.Message}",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }

            if (_settings.Mode == TranscriptionMode.Server && _serverClient is not null)
            {
                var modelName = System.IO.Path.GetFileNameWithoutExtension(modelPath);
                _controller?.ShowStatusPill($"Switching to {modelName}");
                try
                {
                    var ready = await _serverClient.RestartAsync(modelPath).ConfigureAwait(false);
                    if (!ready)
                    {
                        _trayIcon?.ShowBalloon("VoiceType", "Model failed to load; check the log.",
                            System.Windows.Forms.ToolTipIcon.Error);
                    }
                }
                finally
                {
                    _controller?.CloseStatusPill();
                }
            }

            _controller?.RefreshModelName();
        }

        /// <summary>
        /// Re-registers the global hotkey from the (already-updated) shared settings so a hotkey
        /// change made in Settings takes effect immediately, without restarting the app.
        /// </summary>
        public void ReapplyHotkey()
        {
            try
            {
                _hotkeyManager?.UpdateHotkey();
                _toggleHotkeyManager?.UpdateHotkey();

                // Honor the enable flags: install or remove each hook to match the current setting.
                if (_hotkeyManager is not null)
                {
                    _hotkeyManager.Stop();
                    if (_settings?.DictationHotkeyEnabled == true)
                        _hotkeyManager.Start();
                }

                if (_toggleHotkeyManager is not null)
                {
                    _toggleHotkeyManager.Stop();
                    if (_settings?.ToggleModeEnabled == true)
                        _toggleHotkeyManager.Start();
                }

                Logger.Info("App: hotkey re-registered from settings.");
            }
            catch (Exception ex)
            {
                Logger.Error($"App: failed to re-register hotkey: {ex}");
                _trayIcon?.ShowBalloon("VoiceType", $"Could not update hotkey: {ex.Message}",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }
        }

        /// <summary>
        /// Surfaces a brief tray balloon note. Used by Settings to tell the user when a microphone
        /// change will only take effect on the next dictation session (a live mid-session switch is
        /// avoided because the active capture, wav writer and stream consumers are bound to it).
        /// </summary>
        public void ShowNote(string text)
        {
            _trayIcon?.ShowBalloon("VoiceType", text, System.Windows.Forms.ToolTipIcon.Info);
        }

        /// <summary>
        /// Updates the tray context-menu "Toggle mode" checkbox to match the persisted setting
        /// after the user changes it in the Settings window.
        /// </summary>
        public void SyncTrayToggleMode(bool enabled)
        {
            _trayIcon?.SetToggleModeEnabled(enabled);
        }

        /// <summary>
        /// Updates the tray context-menu "View Transcript History" item visibility to match the
        /// persisted EnableTranscriptHistory setting after the user changes it in the Settings window.
        /// </summary>
        public void SyncTrayHistoryEnabled(bool enabled)
        {
            _trayIcon?.SetHistoryEnabled(enabled);
        }

        /// <summary>
        /// Applies a runtime transcription-mode change: creates and starts a long-lived
        /// whisper-server (wiring it into the controller) when switching into Server mode, or
        /// disposes it when switching out. Other modes need no persistent process. The shared
        /// settings must already reflect <paramref name="newMode"/> before calling.
        /// </summary>
        public async Task ApplyModeAsync(TranscriptionMode newMode)
        {
            if (_settings is null)
                return;

            try
            {
                if (newMode == TranscriptionMode.Server)
                {
                    if (_serverClient is not null)
                        return; // already running

                    var serverClient = new Infrastructure.Whisper.WhisperServerClient(_settings);
                    Resources["WhisperServerClient"] = serverClient;
                    if (_controller is not null)
                        _controller.ServerClient = serverClient;
                    _serverClient = serverClient;

                    _controller?.ShowStatusPill("Starting server");
                    try
                    {
                        var ready = await serverClient.StartAsync().ConfigureAwait(false);
                        if (!ready)
                        {
                            _trayIcon?.ShowBalloon("VoiceType", "Server failed to start; check the log.",
                                System.Windows.Forms.ToolTipIcon.Error);
                        }
                    }
                    finally
                    {
                        _controller?.CloseStatusPill();
                    }
                }
                else
                {
                    if (_serverClient is null)
                        return; // nothing to tear down

                    if (_controller is not null)
                        _controller.ServerClient = null;
                    Resources.Remove("WhisperServerClient");

                    try { _serverClient.Dispose(); }
                    catch (Exception ex) { Logger.Error($"App: error disposing server client on mode change: {ex}"); }
                    _serverClient = null;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"App: failed to apply mode change to {newMode}: {ex}");
                _trayIcon?.ShowBalloon("VoiceType", $"Could not switch mode: {ex.Message}",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }
        }

        /// <summary>
        /// Restarts the long-lived whisper-server so changes to server-launch settings
        /// (executable path, host, port, arguments) take effect immediately. No-op unless the
        /// app is currently in Server mode with a running client. The shared settings must
        /// already reflect the new values before calling.
        /// </summary>
        public async Task RestartServerAsync()
        {
            if (_settings is null || _settings.Mode != TranscriptionMode.Server || _serverClient is null)
                return;

            _controller?.ShowStatusPill("Restarting server");
            try
            {
                var ready = await _serverClient.RestartAsync(_settings.WhisperModelPath).ConfigureAwait(false);
                if (!ready)
                {
                    _trayIcon?.ShowBalloon("VoiceType", "Server failed to restart; check the log.",
                        System.Windows.Forms.ToolTipIcon.Error);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"App: failed to restart server after settings change: {ex}");
                _trayIcon?.ShowBalloon("VoiceType", $"Could not restart server: {ex.Message}",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }
            finally
            {
                _controller?.CloseStatusPill();
            }
        }

        // Deletes leftover "voicetype_session_*.wav" files in the configured temp directory.
        // These are normally deleted right after each dictation finalizes; a file only survives
        // here if the app was killed/crashed mid-session, so it's always safe to remove them all
        // on the next startup.
        private static void CleanupStaleTempWavFiles(VoiceTypeSettings settings)
        {
            try
            {
                var tempDir = settings.TempDirectory ?? "./temp";
                if (!System.IO.Directory.Exists(tempDir)) return;

                var staleFiles = System.IO.Directory.GetFiles(tempDir, "voicetype_session_*.wav");
                foreach (var file in staleFiles)
                {
                    try
                    {
                        System.IO.File.Delete(file);
                        Logger.Info($"Deleted stale temp WAV from previous session: {file}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to delete stale temp WAV '{file}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error sweeping stale temp WAV files: {ex.Message}");
            }
        }

        // Sleep/hibernate leaves no trace in this app's own state: whisper-server.exe can be
        // killed by the OS/drivers during suspend while WhisperServerClient.IsReady still only
        // reflects "process handle hasn't exited", not real liveness. On resume, re-check and
        // transparently restart the server if it's no longer alive so the next dictation doesn't
        // silently fail. StartAsync() is a no-op if the server is still ready.
        private async void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
            if (_serverClient == null) return;

            Logger.Info("System resumed from sleep/hibernate; verifying whisper-server health.");
            try
            {
                if (!_serverClient.IsReady)
                {
                    Logger.Info("whisper-server not ready after resume; restarting.");
                    var restarted = await _serverClient.StartAsync();
                    Logger.Info(restarted
                        ? "whisper-server restarted successfully after resume."
                        : "whisper-server failed to restart after resume; will fall back to CLI.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error verifying/restarting whisper-server after resume: {ex}");
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.Error($"Unhandled UI exception: {e.Exception}");
            _trayIcon?.ShowBalloon("VoiceType error", e.Exception.Message,
                System.Windows.Forms.ToolTipIcon.Error);
            // Keep the app alive; a single failed operation should not kill the tray app.
            e.Handled = true;
        }

        private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Logger.Error($"Unhandled domain exception (terminating={e.IsTerminating}): {ex}");
            _trayIcon?.ShowBalloon("VoiceType error", ex?.Message ?? "An unexpected error occurred.",
                System.Windows.Forms.ToolTipIcon.Error);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;

                // Dispose the tray icon first so it never lingers as a ghost in the notification area.
                _trayIcon?.Dispose();
                _trayIcon = null;

                _foregroundMonitor?.Dispose();
                _foregroundMonitor = null;

                if (Resources.Contains("HotkeyManager") && Resources["HotkeyManager"] is IDisposable hk)
                {
                    hk.Dispose();
                }

                if (Resources.Contains("ToggleHotkeyManager") && Resources["ToggleHotkeyManager"] is IDisposable toggleHk)
                {
                    toggleHk.Dispose();
                }

                if (Resources.Contains("DictationController") && Resources["DictationController"] is IDisposable ctrl)
                {
                    ctrl.Dispose();
                }

                if (Resources.Contains("WhisperServerClient") && Resources["WhisperServerClient"] is IDisposable server)
                {
                    server.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error during shutdown: {ex}");
            }
            finally
            {
                try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
                try { _singleInstanceMutex?.Dispose(); } catch { }
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }
    }
}
