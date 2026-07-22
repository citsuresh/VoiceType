using System;
using VoiceType.Models;

namespace VoiceType.Core.Preview
{
    /// <summary>
    /// Holds the most recently produced <see cref="ComparisonEntry"/> so the post-insertion bulb
    /// and comparison popup can retrieve "the current entry" without threading it through call
    /// sites. Raises <see cref="Changed"/> whenever a new entry is recorded. Thread-safe.
    /// </summary>
    public class TranscriptPreviewState
    {
        private readonly object _sync = new();
        private ComparisonEntry? _current;

        /// <summary>Raised (on the calling thread) whenever a new comparison entry is recorded.</summary>
        public event Action<ComparisonEntry>? Changed;

        /// <summary>The most recent comparison entry, or null if none has been recorded yet.</summary>
        public ComparisonEntry? Current
        {
            get { lock (_sync) { return _current; } }
        }

        public void SetCurrent(ComparisonEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);
            lock (_sync) { _current = entry; }
            Changed?.Invoke(entry);
        }
    }
}
