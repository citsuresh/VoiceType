using System.Windows;

namespace VoiceType
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Load settings and initialize core components
            var settings = Infrastructure.Config.SettingsLoader.Load();
            var controller = new Core.DictationSessionController(settings);
            // store on application resources for later retrieval
            Resources["DictationController"] = controller;
            Resources["Settings"] = settings;

            // Initialize and start global hotkey manager
            var hotkeyManager = new Infrastructure.Hotkeys.GlobalHotkeyManager(settings);
            hotkeyManager.HotkeyPressed += async (s, ev) =>
            {
                try
                {
                    await controller.StartSessionAsync();
                }
                catch { }
            };
            hotkeyManager.HotkeyReleased += async (s, ev) =>
            {
                try
                {
                    await controller.StopSessionAsync();
                }
                catch { }
            };

            hotkeyManager.Start();
            Resources["HotkeyManager"] = hotkeyManager;
        }
    }
}
