using System.Threading.Tasks;
using VoiceType.Infrastructure.Config;
using VoiceType.Models;

namespace VoiceType.Infrastructure.Whisper
{
    public class WhisperFinalTranscriber
    {
        private readonly WhisperProcessRunner _runner;

        public WhisperFinalTranscriber(VoiceTypeSettings settings)
        {
            _runner = new WhisperProcessRunner(settings);
        }

        public Task<FinalTranscriptionResult> TranscribeAsync(string wavPath)
        {
            return _runner.RunAsync(wavPath);
        }
    }
}
