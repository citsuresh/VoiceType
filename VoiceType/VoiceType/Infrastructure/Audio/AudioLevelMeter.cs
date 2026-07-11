using System;

namespace VoiceType.Infrastructure.Audio
{
    public class AudioLevelMeter
    {
        // Compute a simple RMS-based level from 16-bit PCM samples
        public static double ComputeLevelFromPcm16(byte[] buffer, int bytesRecorded)
        {
            if (buffer == null || bytesRecorded <= 1) return 0.0;

            long sumSq = 0;
            int samples = bytesRecorded / 2; // 16-bit
            for (int i = 0; i < samples; i++)
            {
                short sample = BitConverter.ToInt16(buffer, i * 2);
                sumSq += (long)sample * sample;
            }

            double rms = Math.Sqrt(sumSq / (double)samples);
            // normalize to 0.0 - 1.0 based on 16-bit max
            return Math.Min(1.0, rms / short.MaxValue);
        }
    }
}
