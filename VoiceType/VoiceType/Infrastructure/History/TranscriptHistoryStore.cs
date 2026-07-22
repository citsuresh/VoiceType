using System.Collections.Generic;
using VoiceType.Models;

namespace VoiceType.Infrastructure.History
{
    /// <summary>
    /// Root JSON document persisted to <c>history.json</c>. A top-level <see cref="Version"/>
    /// supports future format changes without breaking older files.
    /// </summary>
    public class TranscriptHistoryStore
    {
        public int Version { get; set; } = 1;

        public List<ComparisonEntry> Entries { get; set; } = new();
    }
}
