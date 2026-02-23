using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NAudio.Wave;
using Avalonia.Threading;

namespace Frontend.Services;

public class AudioPlayerService : IDisposable
{
    private IWavePlayer? _outputDevice;
    private AudioFileReader? _audioFile;
    private DispatcherTimer? _positionTimer;
    private double _stopAtTime = -1;

    public string? FileHash { get; private set; }
    
    // Data for Visuals (Loaded into RAM)
    public float[]? WaveformData { get; private set; }  // RAW (for Waveform display)
    public double[]? SpectrogramData { get; private set; } // PRE-EMPHASIZED (for Spectrogram only)
    public int SampleRate { get; private set; }

    public event EventHandler<double>? PlaybackPositionChanged;
    public event EventHandler? PlaybackStopped;
    public event EventHandler? FileLoaded;

    public double CurrentTime => _audioFile?.CurrentTime.TotalSeconds ?? 0;
    public double TotalDuration => _audioFile?.TotalTime.TotalSeconds ?? 0;
    public bool IsPlaying => _outputDevice?.PlaybackState == PlaybackState.Playing;

    public AudioPlayerService()
    {
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _positionTimer.Tick += (s, e) =>
        {
            double current = CurrentTime;
            PlaybackPositionChanged?.Invoke(this, current);

            if (_stopAtTime >= 0 && current >= _stopAtTime)
            {
                double stopAt = _stopAtTime;
                Stop();
                PlaybackPositionChanged?.Invoke(this, stopAt);
            }
        };
    }

    public async Task LoadFileAsync(string filePath)
    {
        Stop();
        DisposeAudio();

        FileHash = await Task.Run(() =>
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        });

        await Task.Run(() =>
        {
            try
            {
                // 1. Setup Playback (Keeps raw file stream)
                _audioFile = new AudioFileReader(filePath);
                SampleRate = _audioFile.WaveFormat.SampleRate;

                // 2. Read raw samples into memory for processing
                // We read the whole file to floats [-1.0, 1.0]
                var length = (int)_audioFile.Length / 4;
                var rawSamples = new float[length];
                int read = _audioFile.Read(rawSamples, 0, rawSamples.Length);
                
                // Reset position for playback so the user can play from start immediately
                _audioFile.Position = 0;

                // 3. Prepare Waveform Data (Raw Mono)
                if (_audioFile.WaveFormat.Channels == 2)
                {
                    WaveformData = new float[read / 2];
                    for (int i = 0; i < read; i += 2)
                        WaveformData[i / 2] = (rawSamples[i] + rawSamples[i + 1]) / 2.0f;
                }
                else
                {
                    WaveformData = rawSamples.Take(read).ToArray();
                }

                // 4. Prepare Spectrogram Data (Pre-emphasized Mono)
                SpectrogramData = new double[WaveformData.Length];
                if (WaveformData.Length > 0)
                {
                    // First sample copy
                    SpectrogramData[0] = WaveformData[0];
                    
                    // Pre-emphasis filter: s[i] = s[i] - 0.97 * s[i-1]
                    for (int i = 1; i < WaveformData.Length; i++)
                    {
                        SpectrogramData[i] = WaveformData[i] - 0.97 * WaveformData[i - 1];
                    }
                }

                // 5. Init Playback Device (existing code)
                _outputDevice = new WaveOutEvent { DesiredLatency = 100 };
                _outputDevice.Init(_audioFile);
                _outputDevice.PlaybackStopped += (s, e) => 
                {
                    _positionTimer?.Stop();
                    PlaybackStopped?.Invoke(this, EventArgs.Empty);
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio Load Error: {ex.Message}");
                SpectrogramData = null;
                WaveformData = null;
            }
        });

        FileLoaded?.Invoke(this, EventArgs.Empty);
    }

    public void Play(double startTime, double endTime = -1)
    {
        if (_audioFile == null || _outputDevice == null) return;
        
        if (startTime < 0) startTime = 0;
        if (startTime >= TotalDuration) startTime = 0;

        _stopAtTime = endTime;

        _audioFile.CurrentTime = TimeSpan.FromSeconds(startTime);
        _outputDevice.Play();
        _positionTimer?.Start();
    }

    public void Stop()
    {
        _stopAtTime = -1;
        _outputDevice?.Stop();
        _positionTimer?.Stop();
    }

    public void TogglePlayPause(double currentCursor)
    {
        if (IsPlaying) Stop();
        else Play(currentCursor < 0 ? 0 : currentCursor);
    }

    private void DisposeAudio()
    {
        _outputDevice?.Dispose();
        _outputDevice = null;
        _audioFile?.Dispose();
        _audioFile = null;
        WaveformData = null;
        SpectrogramData = null;
    }

    public void Dispose()
    {
        Stop();
        DisposeAudio();
        _positionTimer?.Stop();
    }
}
