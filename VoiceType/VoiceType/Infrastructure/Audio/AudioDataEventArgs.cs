using System;

namespace VoiceType.Infrastructure.Audio
{
    public class AudioDataEventArgs : EventArgs
    {
        public byte[] Buffer { get; set; } = Array.Empty<byte>();
        public int BytesRecorded { get; set; }
    }
}
