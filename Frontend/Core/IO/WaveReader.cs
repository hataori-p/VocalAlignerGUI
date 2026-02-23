using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Frontend.Core.IO;

public static class WaveReader
{
    public static float[] ReadWav(string path, out int sampleRate, out int channels)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"WAV file not found: {path}");

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        string riff = new(reader.ReadChars(4));
        if (riff != "RIFF") throw new InvalidDataException("Not a RIFF file");
        reader.ReadInt32(); // File size
        string wave = new(reader.ReadChars(4));
        if (wave != "WAVE") throw new InvalidDataException("Not a WAVE file");

        short channelsShort = 0;
        sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? audioData = null;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                reader.ReadInt16(); // AudioFormat
                channelsShort = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // ByteRate
                reader.ReadInt16(); // BlockAlign
                bitsPerSample = reader.ReadInt16();
                int remaining = chunkSize - 16;
                if (remaining > 0) reader.BaseStream.Seek(remaining, SeekOrigin.Current);
            }
            else if (chunkId == "data")
            {
                audioData = reader.ReadBytes(chunkSize);
                 // Handle pad byte if chunk size is odd
                if ((chunkSize & 1) == 1) reader.BaseStream.Seek(1, SeekOrigin.Current);
            }
            else
            {
                reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
            }
        }

        if (audioData == null) throw new InvalidDataException("No data chunk found");
        if (bitsPerSample != 16) throw new NotSupportedException($"Only 16-bit WAV supported, got {bitsPerSample}");

        channels = channelsShort;
        
        var shortSpan = MemoryMarshal.Cast<byte, short>(audioData);
        float[] samples = new float[shortSpan.Length];

        // Normalize 16-bit to float [-1.0, 1.0]
        for (int i = 0; i < shortSpan.Length; i++)
        {
            samples[i] = shortSpan[i] / 32768f;
        }

        return samples;
    }
}
