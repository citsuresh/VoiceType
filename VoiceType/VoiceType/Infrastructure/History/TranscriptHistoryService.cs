using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceType.Infrastructure.Logging;
using VoiceType.Models;

namespace VoiceType.Infrastructure.History
{
    /// <summary>
    /// Loads/saves the bounded transcript comparison history. Entries contain sensitive dictated
    /// text, so the file lives under the user's local-app-data folder (not the install directory,
    /// which may be shared/read-only) and is capped to <see cref="MaxEntries"/> most-recent items.
    /// </summary>
    public class TranscriptHistoryService
    {
        public const int MaxEntries = 50;

        private readonly string _filePath;
        private readonly object _sync = new();
        private TranscriptHistoryStore _store;

        public TranscriptHistoryService(string? filePath = null)
        {
            _filePath = filePath ?? GetDefaultFilePath();
            _store = LoadFromDisk(_filePath);
        }

        /// <summary>
        /// Default location: <c>%LOCALAPPDATA%\VoiceType\history.json</c>.
        /// </summary>
        public static string GetDefaultFilePath()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceType");
            return Path.Combine(dir, "history.json");
        }

        private static JsonSerializerOptions CreateOptions() => new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        private static TranscriptHistoryStore LoadFromDisk(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return new TranscriptHistoryStore();

                var json = File.ReadAllText(filePath);
                var store = JsonSerializer.Deserialize<TranscriptHistoryStore>(json, CreateOptions());
                return store ?? new TranscriptHistoryStore();
            }
            catch (Exception ex)
            {
                Logger.Error($"TranscriptHistoryService: failed to load {filePath}: {ex}");
                return new TranscriptHistoryStore();
            }
        }

        /// <summary>Returns a snapshot of all entries, most-recently-added last.</summary>
        public System.Collections.Generic.IReadOnlyList<ComparisonEntry> GetEntries()
        {
            lock (_sync)
            {
                return _store.Entries.ToArray();
            }
        }

        /// <summary>
        /// Removes all persisted history entries and saves the now-empty store. Safe to call from
        /// any thread.
        /// </summary>
        public void ClearAll()
        {
            lock (_sync)
            {
                _store.Entries.Clear();
                SaveLocked();
            }
        }

        /// <summary>
        /// Appends a new entry and persists to disk, dropping the oldest entries beyond
        /// <see cref="MaxEntries"/>. Safe to call from any thread.
        /// </summary>
        public void AddEntry(ComparisonEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            lock (_sync)
            {
                _store.Entries.Add(entry);
                while (_store.Entries.Count > MaxEntries)
                    _store.Entries.RemoveAt(0);

                SaveLocked();
            }
        }

        private void SaveLocked()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_store, CreateOptions());
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error($"TranscriptHistoryService: failed to save {_filePath}: {ex}");
            }
        }
    }
}
