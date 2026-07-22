namespace VoiceType.Models
{
    /// <summary>
    /// A single highlighted character span within a transcript comparison string, identified by
    /// start index and length (both in UTF-16 char units, matching .NET string indexing) plus the
    /// semantic kind of change it represents. Persisted verbatim so the UI can re-render highlights
    /// against the exact source strings without needing inline markup.
    /// </summary>
    /// <param name="Start">Zero-based start index into the associated source string.</param>
    /// <param name="Length">Length, in characters, of the highlighted span.</param>
    /// <param name="Kind">Semantic classification of the change (removed/modified/added).</param>
    public record HighlightSpan(int Start, int Length, HighlightKind Kind);
}
