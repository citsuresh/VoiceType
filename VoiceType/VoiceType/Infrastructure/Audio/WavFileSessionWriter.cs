using System;
using System.IO;
using System.Threading.Tasks;

namespace VoiceType.Infrastructure.Audio
{
    // Simple WAV writer that writes 16-bit PCM, mono, specified sample rate
    public class WavFileSessionWriter : IDisposable
    {
        private readonly string _filePath;
        private readonly int _sampleRate;
        private readonly int _channels;
        private FileStream? _fs;
        private long _dataChunkSizePos;
        private int _bytesWritten;

        public string FilePath => _filePath;

        public WavFileSessionWriter(string filePath, int sampleRate = 16000, int channels = 1)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _sampleRate = sampleRate;
            _channels = channels;
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");
            _fs = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            WriteHeader();
        }

        private void WriteHeader()
        {
            if (_fs == null) throw new InvalidOperationException();
            using var bw = new BinaryWriter(_fs, System.Text.Encoding.UTF8, leaveOpen: true);
            // RIFF header
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(0); // placeholder for file size
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            // fmt chunk
            bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // PCM
            bw.Write((short)1); // audio format PCM
            bw.Write((short)_channels);
            bw.Write(_sampleRate);
            int byteRate = _sampleRate * _channels * 2;
            bw.Write(byteRate);
            bw.Write((short)(_channels * 2));
            bw.Write((short)16); // bits per sample
            // data chunk header placeholder
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            _dataChunkSizePos = _fs.Position;
            bw.Write(0); // placeholder for data chunk size
        }

        public void AppendPcm16(byte[] buffer, int count)
        {
            if (_fs == null) throw new ObjectDisposedException(nameof(WavFileSessionWriter));
            _fs.Write(buffer, 0, count);
            _bytesWritten += count;
        }

        public void Dispose()
        {
            if (_fs == null) return;
            try
            {
                // finalize sizes
                _fs.Flush();
                using var bw = new BinaryWriter(_fs, System.Text.Encoding.UTF8, leaveOpen: true);
                // write data chunk size
                _fs.Position = _dataChunkSizePos;
                bw.Write(_bytesWritten);
                // write RIFF file size
                _fs.Position = 4;
                bw.Write((int)(_fs.Length - 8));
            }
            finally
            {
                _fs.Dispose();
                _fs = null;
            }
        }
    }
}
