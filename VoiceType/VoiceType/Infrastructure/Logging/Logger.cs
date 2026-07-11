using System;
using System.Diagnostics;
using System.IO;

namespace VoiceType.Infrastructure.Logging
{
    public static class Logger
    {
        private static readonly string LogFile = Path.Combine(AppContext.BaseDirectory, "voicetype.log");
        public static void Info(string message)
        {
            try
            {
                Debug.WriteLine($"[INFO] {DateTime.Now:O} {message}");
                Console.WriteLine($"[INFO] {DateTime.Now:O} {message}");
                try { File.AppendAllText(LogFile, $"[INFO] {DateTime.Now:O} {message}{Environment.NewLine}"); } catch { }
            }
            catch { }
        }

        public static void Error(string message)
        {
            try
            {
                Debug.WriteLine($"[ERROR] {DateTime.Now:O} {message}");
                Console.Error.WriteLine($"[ERROR] {DateTime.Now:O} {message}");
                try { File.AppendAllText(LogFile, $"[ERROR] {DateTime.Now:O} {message}{Environment.NewLine}"); } catch { }
            }
            catch { }
        }
    }
}
