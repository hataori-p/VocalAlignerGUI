using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Frontend.Models;
using NAudio.Wave;

namespace Frontend.Services.Audio;

public class VadService
{
    // Configuration
    private const double FrameDurationSec = 0.020;  // 20ms analysis window
    private const float SpeechThresholdDb = -35.0f; // Threshold to trigger 'Speech' start
    private const float SilenceThresholdDb = -45.0f; // Threshold to likely be 'Silence'
    private const double MinSpeechDuration = 0.100; // Ignore blips shorter than 100ms
    private const double HangoverTime = 0.150;      // Time to wait before declaring silence (smoothing)

    private void Log(string message)
    {
        try { File.AppendAllText("smart_align_log.txt", $"{DateTime.Now:HH:mm:ss} [VAD] {message}\n"); } catch { }
    }

    /// <summary>
    /// Reads the audio file and performs energy-based Voice Activity Detection.
    /// </summary>
    public List<VadSegment> DetectSegments(string filePath)
    {
        Log($"Analyzing file: {filePath}");
        try
        {
            var samples = ReadAudioFile(filePath, out int sampleRate);
            Log($"Read {samples.Length} samples at {sampleRate}Hz.");
            return AnalyzeEnergy(samples, sampleRate);
        }
        catch (Exception ex)
        {
            Log($"VAD Error: {ex}");
            return new List<VadSegment>();
        }
    }

    /// <summary>
    /// Reads input file into a normalized float array (Mono).
    /// </summary>
    private float[] ReadAudioFile(string filePath, out int sampleRate)
    {
        using var reader = new AudioFileReader(filePath);
        sampleRate = reader.WaveFormat.SampleRate;

        // Allocate buffer
        var sampleCount = (int)(reader.Length / (reader.WaveFormat.BitsPerSample / 8));
        var buffer = new float[sampleCount];

        int read = reader.Read(buffer, 0, sampleCount);
        Log($"Buffer info: Requested {sampleCount}, Read {read} samples.");

        // If stereo, AudioFileReader interleaves samples. We need Mono for VAD.
        if (reader.WaveFormat.Channels == 2)
        {
            // Simple averaging stereo to mono
            int monoCount = read / 2;
            var monoBuffer = new float[monoCount];
            for (int i = 0; i < monoCount; i++)
            {
                monoBuffer[i] = (buffer[i * 2] + buffer[i * 2 + 1]) / 2.0f;
            }
            return monoBuffer;
        }

        // Return truncated buffer if we read less than expected
        if (read < sampleCount)
        {
            return buffer.Take(read).ToArray();
        }

        return buffer;
    }

    private List<VadSegment> AnalyzeEnergy(float[] samples, int sampleRate)
    {
        var segments = new List<VadSegment>();
        int samplesPerFrame = (int)(sampleRate * FrameDurationSec);
        
        bool isInsideSpeech = false;
        double speechStartTime = 0;
        double silenceTimer = 0; // Tracks how long we've been below threshold
        double maxDbSoFar = -100;
        
        int totalFrames = samples.Length / samplesPerFrame;

        for (int i = 0; i < totalFrames; i++)
        {
            int offset = i * samplesPerFrame;
            double currentTime = i * FrameDurationSec;

            // 1. Calculate RMS for this frame
            float sumSquares = 0;
            for (int k = 0; k < samplesPerFrame; k++)
            {
                if (offset + k < samples.Length)
                {
                    float val = samples[offset + k];
                    sumSquares += val * val;
                }
            }
            double rms = Math.Sqrt(sumSquares / samplesPerFrame);
            
            // Avoid Log(0)
            double db = rms > 1e-9 ? 20 * Math.Log10(rms) : -100.0;
            if (db > maxDbSoFar) maxDbSoFar = db;

            // 2. State Machine
            if (!isInsideSpeech)
            {
                // Looking for start
                if (db > SpeechThresholdDb)
                {
                    isInsideSpeech = true;
                    speechStartTime = currentTime;
                    silenceTimer = 0;
                }
            }
            else
            {
                // Inside Speech Logic
                if (db < SilenceThresholdDb)
                {
                    // Potential silence
                    silenceTimer += FrameDurationSec;
                    if (silenceTimer > HangoverTime)
                    {
                        // Recognized as silence end
                        double speechEndTime = currentTime - silenceTimer;
                        
                        // Validate duration
                        if ((speechEndTime - speechStartTime) >= MinSpeechDuration)
                        {
                            segments.Add(new VadSegment(speechStartTime, speechEndTime, true));
                        }

                        isInsideSpeech = false;
                        silenceTimer = 0;
                    }
                }
                else
                {
                    // Signal is strong again, reset hangover
                    silenceTimer = 0;
                }
            }
        }

        // Cleanup: If file ended while speaking
        if (isInsideSpeech)
        {
            double fileDuration = (double)samples.Length / sampleRate;
            if ((fileDuration - speechStartTime) >= MinSpeechDuration)
            {
                segments.Add(new VadSegment(speechStartTime, fileDuration, true));
            }
        }

        // 3. Post-Process: Fill gaps with Silence Segments (Optional, useful for Aligner)
        // Note: For now we return only Speech segments. The aligner can infer silence.
        
        Log($"Peak Volume: {maxDbSoFar:F2} dB. Segments found: {segments.Count}");
        return segments;
    }
}
