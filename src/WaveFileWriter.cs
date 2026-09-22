using System;
using System.IO;
using System.Text;

namespace ElevenLabsMusicGenerator
{
    internal static class WaveFileWriter
    {
        public const int Channels = 2;
        public const int BitsPerSample = 16;

        public static void WrapPcmFile(string sourcePath, string destinationPath, int sampleRate)
        {
            var dataLength = new FileInfo(sourcePath).Length;
            if (dataLength <= 0) throw new InvalidDataException("ElevenLabs returned an empty PCM file.");
            if (dataLength > uint.MaxValue - 44) throw new InvalidDataException("The PCM file is too large for a standard WAV container.");

            using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(output, Encoding.ASCII, true))
            using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var blockAlign = Channels * BitsPerSample / 8;
                var byteRate = sampleRate * blockAlign;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write((uint)(36 + dataLength));
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write((uint)16);
                writer.Write((ushort)1);
                writer.Write((ushort)Channels);
                writer.Write((uint)sampleRate);
                writer.Write((uint)byteRate);
                writer.Write((ushort)blockAlign);
                writer.Write((ushort)BitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write((uint)dataLength);
                writer.Flush();
                input.CopyTo(output, 64 * 1024);
            }
        }

        public static bool MatchesGeneratedWave(string path, int sampleRate, int durationSeconds)
        {
            const int headerBytes = 44;
            var expectedDataBytes = (long)sampleRate * Channels * BitsPerSample / 8 * durationSeconds;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(input, Encoding.ASCII))
            {
                if (input.Length != headerBytes + expectedDataBytes) return false;
                return Encoding.ASCII.GetString(reader.ReadBytes(4)) == "RIFF" &&
                    reader.ReadUInt32() == input.Length - 8 &&
                    Encoding.ASCII.GetString(reader.ReadBytes(4)) == "WAVE" &&
                    Encoding.ASCII.GetString(reader.ReadBytes(4)) == "fmt " &&
                    reader.ReadUInt32() == 16 && reader.ReadUInt16() == 1 &&
                    reader.ReadUInt16() == Channels && reader.ReadUInt32() == sampleRate &&
                    reader.ReadUInt32() == sampleRate * Channels * BitsPerSample / 8 &&
                    reader.ReadUInt16() == Channels * BitsPerSample / 8 &&
                    reader.ReadUInt16() == BitsPerSample &&
                    Encoding.ASCII.GetString(reader.ReadBytes(4)) == "data" &&
                    reader.ReadUInt32() == expectedDataBytes;
            }
        }
    }
}
