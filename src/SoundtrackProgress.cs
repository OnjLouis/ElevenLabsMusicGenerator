using System;

namespace ElevenLabsMusicGenerator
{
    internal enum SoundtrackStage { Preparing, Uploading, Waiting, Downloading, Saving }

    internal sealed class SoundtrackProgress
    {
        public readonly SoundtrackStage Stage;
        public readonly long Bytes;
        public readonly long TotalBytes;
        public SoundtrackProgress(SoundtrackStage stage, long bytes = 0, long totalBytes = 0)
        {
            Stage = stage; Bytes = Math.Max(0, bytes); TotalBytes = Math.Max(0, totalBytes);
        }
        public string Message
        {
            get
            {
                switch (Stage)
                {
                    case SoundtrackStage.Uploading: return "Uploading videos.";
                    case SoundtrackStage.Waiting: return "Upload complete. Waiting for ElevenLabs to generate the soundtrack. This can take several minutes, even for a short video. No generation percentage or time remaining is available.";
                    case SoundtrackStage.Downloading: return "Receiving soundtrack from ElevenLabs.";
                    case SoundtrackStage.Saving: return "Saving soundtrack recordings.";
                    default: return "Preparing video upload.";
                }
            }
        }
        public int? Percent
        {
            get { return TotalBytes > 0 && (Stage == SoundtrackStage.Uploading || Stage == SoundtrackStage.Downloading) ? (int?)Math.Min(100, 100.0 * Bytes / TotalBytes) : null; }
        }
        public string Describe(TimeSpan elapsed)
        {
            var text = Message + Environment.NewLine + "Elapsed: " + ElapsedText(elapsed) + ".";
            if (Stage == SoundtrackStage.Uploading || Stage == SoundtrackStage.Downloading)
            {
                text += Environment.NewLine + (Stage == SoundtrackStage.Uploading ? "Sent: " : "Received: ") + Bytes.ToString("N0") + " bytes";
                if (TotalBytes > 0) text += " of " + TotalBytes.ToString("N0") + " (" + Percent + " percent)";
                text += ".";
            }
            return text;
        }
        internal static string ElapsedText(TimeSpan elapsed)
        {
            var total = Math.Max(0, (long)elapsed.TotalSeconds);
            var minutes = total / 60; var seconds = total % 60;
            return (minutes > 0 ? minutes + (minutes == 1 ? " minute, " : " minutes, ") : "") + seconds + (seconds == 1 ? " second" : " seconds");
        }
    }
}
