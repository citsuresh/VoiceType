using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using VoiceType.Infrastructure.Logging;

namespace VoiceType.Infrastructure.Audio
{
    // Captures microphone audio using WaveInEvent and writes PCM samples to a session writer.
    public class AudioCaptureService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly int _deviceNumber;
        private WavFileSessionWriter? _writer;
        private DateTime _lastRawAudioLog = DateTime.MinValue;

        public event EventHandler<double>? AudioLevelUpdated;
        public event EventHandler<string>? SessionFileReady;
        public event EventHandler<AudioDataEventArgs>? RawAudioAvailable;

        /// <summary>
        /// The WaveIn device index this instance captures from. Callers compare this against the
        /// current setting to decide whether the instance must be recreated after a mic change.
        /// </summary>
        public int DeviceNumber => _deviceNumber;

        public AudioCaptureService(int sampleRate = 16000, int channels = 1, int deviceNumber = 0)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _deviceNumber = deviceNumber;
            try { Logger.Info($"AudioCaptureService created: {this.GetHashCode()}, sampleRate={sampleRate}, channels={channels}, device={deviceNumber}"); } catch { }
        }

        // Start with optional wav output path; pass null to avoid file creation (stream-only)
        public void Start(string outputFilePath)
        {
            Stop();

            try { Logger.Info($"AudioCaptureService.Start called: {this.GetHashCode()}, outputFilePath='{outputFilePath}'"); } catch { }

            if (!string.IsNullOrEmpty(outputFilePath))
            {
                _writer = new WavFileSessionWriter(outputFilePath, _sampleRate, _channels);
            }

            _waveIn = new WaveInEvent
            {
                DeviceNumber = _deviceNumber,
                WaveFormat = new WaveFormat(_sampleRate, 16, _channels),
                BufferMilliseconds = 100
            };

            _waveIn.DataAvailable += WaveIn_DataAvailable;
            _waveIn.RecordingStopped += WaveIn_RecordingStopped;
            _waveIn.StartRecording();
        }

        private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
        {
            // append raw pcm bytes to wav file if writer present
            _writer?.AppendPcm16(e.Buffer, e.BytesRecorded);

            // raise raw PCM available for stream consumers
            try
            {
                var copy = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, 0, copy, 0, e.BytesRecorded);
                RawAudioAvailable?.Invoke(this, new AudioDataEventArgs { Buffer = copy, BytesRecorded = e.BytesRecorded });
            }
            catch { }

            // compute audio level
            var level = AudioLevelMeter.ComputeLevelFromPcm16(e.Buffer, e.BytesRecorded);
            AudioLevelUpdated?.Invoke(this, level);

            // diagnostic: log raw audio availability at most once per second
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _lastRawAudioLog).TotalSeconds >= 1)
                {
                    _lastRawAudioLog = now;
                    VoiceType.Infrastructure.Logging.Logger.Info($"WaveIn: bytes={e.BytesRecorded}, level={level:N3}");
                }
            }
            catch { }
        }

        private void WaveIn_RecordingStopped(object? sender, StoppedEventArgs e)
        {
            // finalize writer and notify file ready
            try
            {
                _writer?.Dispose();
                if (_writer != null)
                {
                    SessionFileReady?.Invoke(this, _writer.FilePath);
                }
            }
            catch { }
        }

        public void Stop()
        {
            if (_waveIn != null)
            {
                try
                {
                    _waveIn.DataAvailable -= WaveIn_DataAvailable;
                    _waveIn.RecordingStopped -= WaveIn_RecordingStopped;
                    _waveIn.StopRecording();
                }
                catch { }
                finally
                {
                    _waveIn.Dispose();
                    _waveIn = null;
                }
            }

            if (_writer != null)
            {
                try { _writer.Dispose(); }
                catch { }
                finally { _writer = null; }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
