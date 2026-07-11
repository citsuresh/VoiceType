using System;
using System.Threading.Tasks;
using System.Windows;
using VoiceType.Infrastructure.Logging;

namespace VoiceType.Infrastructure.Input
{
    public static class ClipboardHelper
    {
        public class ClipboardBackup
        {
            public string? Text { get; set; }
            public bool HadText { get; set; }
        }

        public static Task<ClipboardBackup> BackupClipboardAsync()
        {
            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var backup = new ClipboardBackup();
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        backup.Text = Clipboard.GetText();
                        backup.HadText = true;
                    }
                }
                catch { }
                return backup;
            }).Task;
        }

        public static Task<bool> SetTextAsync(string text)
        {
            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Retry the set: another process can briefly hold the clipboard lock, which
                // makes a single SetText/SetDataObject throw and leave the OLD content in
                // place (causing a stale paste). Retrying handles that transient contention.
                Exception? last = null;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        Clipboard.SetDataObject(text, true);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        System.Threading.Thread.Sleep(100);
                    }
                }

                Logger.Error($"ClipboardHelper.SetTextAsync failed after retries: {last?.Message}");
                return false;
            }).Task;
        }

        public static Task<string?> GetTextAsync()
        {
            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    return Clipboard.ContainsText() ? Clipboard.GetText() : null;
                }
                catch (Exception ex)
                {
                    Logger.Error($"ClipboardHelper.GetTextAsync failed: {ex.Message}");
                    return null;
                }
            }).Task;
        }

        public static Task RestoreAsync(ClipboardBackup backup)
        {
            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (backup == null) return;
                    if (backup.HadText && backup.Text != null)
                    {
                        Clipboard.SetText(backup.Text);
                    }
                    else
                    {
                        Clipboard.Clear();
                    }
                }
                catch { }
            }).Task;
        }
    }
}
