using System;
using System.Collections.Generic;

namespace VoiceType.Models
{
    /// <summary>
    /// One transcript comparison entry: the raw Whisper output ("You spoke"), the post-processed
    /// text that was actually inserted ("Final text"), and the semantic highlight spans describing
    /// how they differ. Used both for the post-insertion comparison popup and for persisted history.
    /// </summary>
    /// <param name="Id">Stable identity for the entry (also used as a React-style list key in the UI).</param>
    /// <param name="CreatedUtc">UTC timestamp the entry was created.</param>
    /// <param name="SpokenText">Raw transcript text, before <c>CleanTranscript</c> post-processing.</param>
    /// <param name="FinalText">Post-processed text that was inserted into the target application.</param>
    /// <param name="SpokenHighlights">Highlight spans (removed/modified) within <see cref="SpokenText"/>.</param>
    /// <param name="FinalHighlights">Highlight spans (modified/added) within <see cref="FinalText"/>.</param>
    /// <param name="ModelName">Optional display name of the Whisper model used, for metadata display.</param>
    public record ComparisonEntry(
        Guid Id,
        DateTime CreatedUtc,
        string SpokenText,
        string FinalText,
        List<HighlightSpan> SpokenHighlights,
        List<HighlightSpan> FinalHighlights,
        string? ModelName = null);
}
