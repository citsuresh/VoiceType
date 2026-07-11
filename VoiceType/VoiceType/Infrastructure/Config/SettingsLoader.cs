using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace VoiceType.Infrastructure.Config
{
    public static class SettingsLoader
    {
        private const string DefaultFile = "appsettings.json";

        // Shared options so Load and Save round-trip identically: case-insensitive property
        // matching, enums written as strings (with integer fallback on read), and indented
        // output that keeps the persisted appsettings.json human-readable.
        private static JsonSerializerOptions CreateOptions() => new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
        };

        public static VoiceTypeSettings Load(string? path = null)
        {
            var file = path ?? DefaultFile;
            if (!File.Exists(file))
            {
                // return defaults
                return new VoiceTypeSettings();
            }

            try
            {
                var json = File.ReadAllText(file);
                var settings = JsonSerializer.Deserialize<VoiceTypeSettings>(json, CreateOptions());
                if (settings is null)
                    return new VoiceTypeSettings();

                MigrateLegacyHotkey(settings);
                return settings;
            }
            catch (Exception)
            {
                // ignore and return defaults for now
                return new VoiceTypeSettings();
            }
        }

        // Older appsettings.json files stored the hotkey as separate "HotkeyKey" and
        // "HotkeyModifiers" fields. Those are now unknown properties and land in ExtensionData;
        // fold them into the single DictationHotkey field and drop them so a subsequent save
        // writes only the new key.
        private static void MigrateLegacyHotkey(VoiceTypeSettings settings)
        {
            var ext = settings.ExtensionData;
            if (ext is null)
                return;

            var hasKey = ext.TryGetValue("HotkeyKey", out var keyEl);
            var hasMods = ext.TryGetValue("HotkeyModifiers", out var modsEl);
            if (!hasKey && !hasMods)
                return;

            var key = hasKey && keyEl.ValueKind == JsonValueKind.String ? keyEl.GetString() : null;
            var mods = hasMods && modsEl.ValueKind == JsonValueKind.String ? modsEl.GetString() : null;

            if (!string.IsNullOrWhiteSpace(key))
                settings.DictationHotkey = VoiceTypeSettings.CombineHotkey(mods, key);

            ext.Remove("HotkeyKey");
            ext.Remove("HotkeyModifiers");
        }

        /// <summary>
        /// Serializes <paramref name="settings"/> back to <paramref name="path"/> (defaults to
        /// appsettings.json) using the same JSON options as <see cref="Load"/>, so enums stay as
        /// strings and the file remains indented.
        /// </summary>
        public static async Task SaveAsync(VoiceTypeSettings settings, string? path = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var file = path ?? DefaultFile;
            var json = JsonSerializer.Serialize(settings, CreateOptions());

            // Write to a sibling temp file first, then atomically replace the target. This avoids
            // leaving a truncated/corrupt appsettings.json if the process is interrupted mid-write.
            var tempFile = file + ".tmp";
            await File.WriteAllTextAsync(tempFile, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempFile, file, overwrite: true);
        }

        /// <summary>
        /// Synchronous counterpart to <see cref="SaveAsync"/> for callers on a non-async path.
        /// </summary>
        public static void Save(VoiceTypeSettings settings, string? path = null)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var file = path ?? DefaultFile;
            var json = JsonSerializer.Serialize(settings, CreateOptions());

            var tempFile = file + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, file, overwrite: true);
        }
    }
}
