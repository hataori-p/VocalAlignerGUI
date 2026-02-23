using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Frontend.Services;

public class AppState
{
    [JsonPropertyName("lastAudioPath")]
    public string? LastAudioPath { get; set; }

    [JsonPropertyName("lastTextGridPath")]
    public string? LastTextGridPath { get; set; }

    [JsonPropertyName("selectedModelId")]
    public string? SelectedModelId { get; set; }

    [JsonPropertyName("modelSelectionSaved")]
    public bool ModelSelectionSaved { get; set; }

    [JsonPropertyName("lastUsedDir")]
    public string? LastUsedDir { get; set; }

    [JsonPropertyName("isMaximized")]
    public bool IsMaximized { get; set; }

    [JsonPropertyName("windowX")]
    public double WindowX { get; set; } = 100;

    [JsonPropertyName("windowY")]
    public double WindowY { get; set; } = 100;

    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; } = 1280;

    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; } = 720;

    [JsonPropertyName("timelineVisibleStart")]
    public double TimelineVisibleStart { get; set; }

    [JsonPropertyName("timelineVisibleEnd")]
    public double TimelineVisibleEnd { get; set; }

    [JsonPropertyName("playheadPosition")]
    public double PlayheadPosition { get; set; }
}

public static class AppStateService
{
    private static readonly string _stateDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "VocalAlignerGUI");

    private static readonly string _statePath =
        Path.Combine(_stateDir, "app_state.json");

    private static readonly JsonSerializerOptions _jsonOpts =
        new() { WriteIndented = true };

    private static readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

    public static AppState Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                Current = new AppState();
                return;
            }

            string json = File.ReadAllText(_statePath);
            Current = JsonSerializer.Deserialize<AppState>(json, _jsonOpts) ?? new AppState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppState] Load failed: {ex.Message}");
            Current = new AppState();
        }
    }

    public static async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                Current = new AppState();
                return;
            }

            string json = await File.ReadAllTextAsync(_statePath);
            Current = JsonSerializer.Deserialize<AppState>(json, _jsonOpts) ?? new AppState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppState] Load failed: {ex.Message}");
            Current = new AppState();
        }
    }

    public static async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(_stateDir);
            string json = JsonSerializer.Serialize(Current, _jsonOpts);
            await File.WriteAllTextAsync(_statePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppState] Save failed: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// Updates LastUsedDir from a full file path and saves immediately.
    /// </summary>
    public static async Task UpdateFromFileAsync(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Current.LastUsedDir = dir;
            await SaveAsync();
        }
    }
}
