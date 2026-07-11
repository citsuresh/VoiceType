namespace VoiceType.Models
{
    public class FinalTranscriptionResult
    {
        public bool Success { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public int ExitCode { get; set; }

        // True when the transcription request was aborted because it exceeded the configured
        // timeout (as opposed to a genuine failure or empty result). Lets callers show an
        // accurate "timed out" message instead of a generic failure or "no speech" notice.
        public bool TimedOut { get; set; }
    }
}
