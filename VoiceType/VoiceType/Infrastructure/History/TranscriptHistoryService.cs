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
        public const int DefaultMaxEntries = 50;

        private readonly string _filePath;
        private readonly object _sync = new();
        private TranscriptHistoryStore _store;

        /// <summary>
        /// Maximum number of entries retained on disk. Defaults to <see cref="DefaultMaxEntries"/>
        /// but can be changed at runtime via <see cref="SetMaxEntries"/> (e.g. from a user-configurable
        /// retention-limit setting).
        /// </summary>
        public int MaxEntries { get; private set; } = DefaultMaxEntries;

        public TranscriptHistoryService(string? filePath = null, int maxEntries = DefaultMaxEntries)
        {
            _filePath = filePath ?? GetDefaultFilePath();
            MaxEntries = maxEntries > 0 ? maxEntries : DefaultMaxEntries;
            _store = LoadFromDisk(_filePath);
        }

        /// <summary>
        /// Updates the retention limit and immediately trims any excess oldest entries, saving the
        /// result. Safe to call from any thread; used when the user changes the retention-limit
        /// setting without restarting the app.
        /// </summary>
        public void SetMaxEntries(int maxEntries)
        {
            if (maxEntries <= 0) maxEntries = DefaultMaxEntries;
            lock (_sync)
            {
                MaxEntries = maxEntries;
                var trimmed = false;
                while (_store.Entries.Count > MaxEntries)
                {
                    _store.Entries.RemoveAt(0);
                    trimmed = true;
                }

                if (trimmed)
                    SaveLocked();
            }
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
