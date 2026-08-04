using System;
using System.Diagnostics;
using System.IO;

namespace VoiceType.Infrastructure.Logging
{
    public static class Logger
    {
        private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "voicetype.log");

        // Rotated-out copy of the log, kept as a single backup so total on-disk usage stays
        // bounded to roughly 2x MaxLogSizeBytes rather than growing forever.
        private static readonly string BackupLogFile = LogFile + ".old";
        private const long MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB

        // Serializes rotation + append so concurrent writers (multiple components now log to
        // this same file) can't interleave/corrupt a write or race the rotation check.
        private static readonly object WriteLock = new object();

        public static void Info(string message)
        {
            try
            {
                Debug.WriteLine($"[INFO] {DateTime.Now:O} {message}");
                Console.WriteLine($"[INFO] {DateTime.Now:O} {message}");
                WriteLine($"[INFO] {DateTime.Now:O} {message}");
            }
            catch { }
        }

        public static void Error(string message)
        {
            try
            {
                Debug.WriteLine($"[ERROR] {DateTime.Now:O} {message}");
                Console.Error.WriteLine($"[ERROR] {DateTime.Now:O} {message}");
                WriteLine($"[ERROR] {DateTime.Now:O} {message}");
            }
            catch { }
        }

        // Appends one line to voicetype.log, rotating it out to voicetype.log.old first if it
        // has grown past MaxLogSizeBytes. Best-effort: any failure here is swallowed so logging
        // itself never breaks the app.
        private static void WriteLine(string line)
        {
            lock (WriteLock)
            {
                try
                {
                    RotateIfNeeded();
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                }
                catch { }
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                var info = new FileInfo(LogFile);
                if (!info.Exists || info.Length < MaxLogSizeBytes) return;

                try { File.Delete(BackupLogFile); } catch { }
                File.Move(LogFile, BackupLogFile);
            }
            catch { }
        }
    }
}
