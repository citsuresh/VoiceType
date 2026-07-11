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

            var settings = SettingsLoader.Load();
            var controller = new DictationSessionController(settings);
            Resources["DictationController"] = controller;
            Resources["Settings"] = settings;
            _settings = settings;
            _controller = controller;

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
                try { await controller.StartSessionAsync(); } catch { }
            };
            hotkeyManager.HotkeyReleased += async (s, ev) =>
            {
                Logger.Info("Hotkey released");
                try { await controller.StopSessionAsync(); } catch { }
            };

            hotkeyManager.Start();
            Resources["HotkeyManager"] = hotkeyManager;
            _hotkeyManager = hotkeyManager;

            // Tray icon is the sole control center for this windowless app.
            _trayIcon = new TrayIconManager(
                onOpenSettings: () => SettingsWindow.ShowSingleInstance(),
                onExit: () => Shutdown(),
                getModels: () => new Infrastructure.Whisper.WhisperProcessRunner(settings).EnumerateModels(),
                getActiveModel: () => settings.WhisperModelPath,
                onModelSelected: SwitchModelAsync);
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
                // Dispose the tray icon first so it never lingers as a ghost in the notification area.
                _trayIcon?.Dispose();
                _trayIcon = null;

                if (Resources.Contains("HotkeyManager") && Resources["HotkeyManager"] is IDisposable hk)
                {
                    hk.Dispose();
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
