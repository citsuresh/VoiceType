namespace VoiceType.Models
{
    /// <summary>
    /// Semantic classification of a highlighted token span within a transcript comparison.
    /// The UI maps these to theme brushes rather than persisting literal colors.
    /// </summary>
    public enum HighlightKind
    {
        /// <summary>Token present only in the "You spoke" text (rendered red).</summary>
        Removed,

        /// <summary>Token changed between "You spoke" and "Final text" (rendered yellow in both).</summary>
        Modified,

        /// <summary>Token present only in the "Final text" (rendered green).</summary>
        Added
    }
}
