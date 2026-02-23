using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Frontend.Models;
using Frontend.Services;
using Frontend.Services.Logging;
using Frontend.Services.Alignment;
using Frontend.Services.Scripting;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Frontend.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public AudioPlayerService AudioPlayer { get; }

    [ObservableProperty]
    private string _statusMessage = "Ready";

    private NativeAlignmentService _nativeAligner = new();
    private readonly PhonemizerScriptingService _phonemizerService = new();

    [ObservableProperty]
    private bool _isDirty;

    private readonly string _appVersion;

    public bool IsServerConnected => _nativeAligner.IsAvailable;

    public bool IsModelLoaded => SelectedModel != null && IsServerConnected;

    public bool HasOnnxModel =>
        SelectedModel != null &&
        !SelectedModel.IsManualMode &&
        IsServerConnected;

    public bool IsManualMode =>
        SelectedModel != null &&
        SelectedModel.IsManualMode;

    public bool CanInsertSilenceToSelection => MainTier != null && Timeline != null && Timeline.IsSelectionActive;

    [ObservableProperty]
    private string _currentTextGridPath = string.Empty;

    [ObservableProperty]
    private bool _isSearchVisible; // Controls Find overlay visibility

    [ObservableProperty]
    private string _searchQuery = string.Empty; // Current search text

    [ObservableProperty]
    private bool _searchExactMatch; // false = Substring (Default), true = Whole String

    [ObservableProperty]
    private bool _searchCaseSensitive; // false = Insensitive (Default), true = Sensitive

    private string? _currentAudioPath;
    private readonly SmartAligner _smartAligner = new();
    private readonly HashSet<string> _tierPriorityNames = new() { "phons", "phonemes", "phones", "notes", "words" };

    public TimelineState Timeline { get; } = new();

    public string WindowTitle
    {
        get
        {
            string dirtyMarker = IsDirty ? " *" : string.Empty;
            return $"VocalAlignerGUI v{_appVersion}{dirtyMarker}";
        }
    }

    public string? LastUsedDir => AppStateService.Current.LastUsedDir;

    [ObservableProperty]
    private TextGrid? _mainTier = new();

    [ObservableProperty]
    private List<string> _recognitionPhonemes = new();

    [ObservableProperty]
    private ObservableCollection<string> _availablePhonemizers = new();

    [ObservableProperty]
    private string? _selectedPhonemizer;

    [ObservableProperty]
    private ObservableCollection<IModelProfile?> _availableModels = new();

    [ObservableProperty]
    private IModelProfile? _selectedModel;

    private readonly HashSet<TextInterval> _trackedIntervals = new();
    private readonly HashSet<AlignmentBoundary> _trackedBoundaries = new();
    private TextGrid? _subscribedGrid;
    private bool _suppressDirtyNotifications;
    private bool _suppressModelSave;

    [ObservableProperty]
    private bool _isFileLoggingActive;

    [ObservableProperty]
    private string _fileLoggingPath = string.Empty;

    private const string FallbackAudioPath = @"C:\Projekty\VocalAlignerGUI\data\test_audio.wav";

    public MainWindowViewModel()
    {
        // Fetch the version from the Assembly metadata
        var assembly = Assembly.GetExecutingAssembly();
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Strip the git commit hash suffix (everything after '+')
        if (infoVersion != null && infoVersion.Contains('+'))
            infoVersion = infoVersion[..infoVersion.IndexOf('+')];

        // Fallback logic if attribute is missing
        _appVersion = infoVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0";

        AudioPlayer = new AudioPlayerService();
        _nativeAligner.OnAvailabilityChanged = () =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(IsServerConnected));
                OnPropertyChanged(nameof(HasOnnxModel));
                OnPropertyChanged(nameof(IsManualMode));
            });
        };
        AudioPlayer.FileLoaded += (s, e) =>
        {
            Timeline.TotalDuration = AudioPlayer.TotalDuration;
        };
        AudioPlayer.PlaybackPositionChanged += (s, time) => Timeline.PlaybackPosition = time;

        AudioPlayer.PlaybackStopped += (s, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Timeline.PlaybackPosition = _startPlaybackTime;
            });
        };

        Timeline.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TimelineState.IsSelectionActive))
            {
                OnPropertyChanged(nameof(CanInsertSilenceToSelection));
                InsertSilenceToSelectionCommand.NotifyCanExecuteChanged();
            }
        };

        SubscribeToGrid(_mainTier);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        // AppState already loaded synchronously in MainWindow constructor
        await InitializePhonemizerServiceAsync();

        // Restore last audio
        string? audioToLoad = AppStateService.Current.LastAudioPath;
        if (string.IsNullOrEmpty(audioToLoad) || !File.Exists(audioToLoad))
            audioToLoad = File.Exists(FallbackAudioPath) ? FallbackAudioPath : null;

        if (audioToLoad != null)
            await LoadAudio(audioToLoad);

        // Restore last TextGrid independently (may differ from audio sibling)
        string? tgToLoad = AppStateService.Current.LastTextGridPath;
        if (!string.IsNullOrEmpty(tgToLoad) && File.Exists(tgToLoad))
        {
            // Only load if it wasn't already loaded by the audio sibling auto-detect
            if (!string.Equals(CurrentTextGridPath, tgToLoad, StringComparison.OrdinalIgnoreCase))
                await LoadTextGrid(tgToLoad);
        }

        RestoreTimelineState();
    }

    private void RestoreTimelineState()
    {
        var state = AppStateService.Current;
        double total = Timeline.TotalDuration;

        // Restore playhead
        if (state.PlayheadPosition > 0 && (total <= 0 || state.PlayheadPosition <= total))
            Timeline.PlaybackPosition = state.PlayheadPosition;

        // Restore zoom window — only if audio was loaded and values are valid
        if (total > 0
            && state.TimelineVisibleEnd > state.TimelineVisibleStart
            && state.TimelineVisibleStart >= 0
            && state.TimelineVisibleEnd <= total + 0.01)
        {
            Timeline.SetView(state.TimelineVisibleStart, state.TimelineVisibleEnd);
        }
    }

    public void SaveTimelineState()
    {
        AppStateService.Current.TimelineVisibleStart = Timeline.VisibleStartTime;
        AppStateService.Current.TimelineVisibleEnd   = Timeline.VisibleEndTime;
        AppStateService.Current.PlayheadPosition     = Timeline.PlaybackPosition;
        _ = AppStateService.SaveAsync();
    }

    private async Task InitializePhonemizerServiceAsync()
    {
        _phonemizerService.ActiveModelChanged += RefreshPhonemizerDropdown;
        await _phonemizerService.LoadAllProfilesAsync();
        await _phonemizerService.LoadAllModelProfilesAsync();

        AvailableModels.Clear();
        AvailableModels.Add(null);
        foreach (var model in _phonemizerService.ModelProfiles.Values)
            AvailableModels.Add(model);

        // Suppress the OnSelectedModelChanged save during init
        _suppressModelSave = true;
        try
        {
            IModelProfile? autoSelect = null;
            string? savedId = AppStateService.Current.SelectedModelId;

            if (AppStateService.Current.ModelSelectionSaved)
            {
                // User has explicitly chosen before — respect it, even if null
                if (!string.IsNullOrEmpty(savedId))
                {
                    autoSelect = _phonemizerService.ModelProfiles.Values
                        .FirstOrDefault(m => m.Id == savedId &&
                            (m.IsManualMode || File.Exists(Path.Combine(AppContext.BaseDirectory, m.ModelFile))));
                }
                // else: savedId is null/empty → user explicitly chose "Not Assigned", leave autoSelect = null
            }
            else
            {
                // First run — auto-pick first available model
                autoSelect = _phonemizerService.ModelProfiles.Values
                    .FirstOrDefault(m => m.IsManualMode || File.Exists(
                        Path.Combine(AppContext.BaseDirectory, m.ModelFile)));
            }

            SelectedModel = autoSelect;
        }
        finally
        {
            _suppressModelSave = false;
        }

        RefreshPhonemizerDropdown();

        if (MainTier != null && MainTier.Intervals.Count > 0)
            ValidateGrid();

        OnPropertyChanged(nameof(WindowTitle));
    }

    private string StripCompatibilityIndicator(string displayName) =>
        displayName.Length > 2 && displayName[1] == ' '
            ? displayName[2..]
            : displayName;

    private void RefreshPhonemizerDropdown()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            AvailablePhonemizers.Clear();

            foreach (var profile in _phonemizerService.Profiles.Values)
            {
                var level = _phonemizerService.GetCompatibilityLevel(profile.Id);

                string indicator = level switch
                {
                    CompatibilityLevel.Full => "✓ ",
                    CompatibilityLevel.Partial => "~ ",
                    CompatibilityLevel.Incompatible => "✗ ",
                    _ => "  "
                };

                AvailablePhonemizers.Add($"{indicator}{profile.DisplayName}");
            }

            // Select first fully compatible profile by default if available
            SelectedPhonemizer = AvailablePhonemizers
                .FirstOrDefault(p => p.StartsWith("✓"))
                ?? AvailablePhonemizers.FirstOrDefault();
        });
    }

    [RelayCommand]
    public async Task LoadAudio(string? path = null)
    {
        try
        {
            string finalPath = path ?? FallbackAudioPath;
            _currentAudioPath = finalPath;

            if (!File.Exists(finalPath))
            {
                StatusMessage = $"File not found: {finalPath}";
                return;
            }

            CurrentTextGridPath = string.Empty;
            StatusMessage = "Loading audio...";
            await AudioPlayer.LoadFileAsync(finalPath);

            StatusMessage = "Loading phonemizers...";
            if (_phonemizerService.Profiles.Count == 0)
            {
                await _phonemizerService.LoadAllProfilesAsync();
                await _phonemizerService.LoadAllModelProfilesAsync();
            }

            Timeline.TotalDuration = AudioPlayer.TotalDuration;
            Timeline.CurrentFileHash = AudioPlayer.FileHash ?? Guid.NewGuid().ToString();
            Timeline.VisibleStartTime = 0;
            Timeline.VisibleEndTime = AudioPlayer.TotalDuration;

            RecognitionPhonemes = new();
            StatusMessage = $"Loaded: {Path.GetFileName(finalPath)}";

            AppStateService.Current.LastAudioPath = finalPath;
            AppStateService.Current.LastUsedDir = Path.GetDirectoryName(finalPath);
            _ = AppStateService.SaveAsync();

            string tgPath = Path.ChangeExtension(finalPath, ".TextGrid");

            if (File.Exists(tgPath))
            {
                StatusMessage += " | Loading TG...";
                var grid = await TextGridParser.ParseAsync(tgPath);

                if (grid != null && grid.Intervals.Count > 0)
                {
                    foreach (var boundary in grid.Boundaries)
                        boundary.IsLocked = true;

                    MainTier = grid;
                    CurrentTextGridPath = tgPath;
                    StatusMessage = "Loaded Audio & TextGrid";
                    ValidateGrid();
                }
                else
                {
                    StatusMessage = "TG Load Failed (Empty/Error)";
                    CurrentTextGridPath = string.Empty;
                    LoadEmptyGrid(AudioPlayer.TotalDuration);
                }
            }
            else
            {
                StatusMessage += " | No TG found";
                CurrentTextGridPath = string.Empty;
                LoadEmptyGrid(AudioPlayer.TotalDuration);
            }

            // Automatically run recognition when the backend is ready so users see results immediately.
            await RunRecognition();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    [RelayCommand]
    public async Task ImportText(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                StatusMessage = $"File not found: {path}";
                return;
            }

            string text = await File.ReadAllTextAsync(path);

            // Basic cleanup
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            if (!string.IsNullOrEmpty(_currentAudioPath))
            {
                StatusMessage = "Smart Aligning (VAD + DP)...";
                try
                {
                    System.IO.File.AppendAllText("smart_align_log.txt", $"\n=== New Import: {DateTime.Now} ===\nAudio: {_currentAudioPath}\nText: {path}\n");
                    
                    var smartGrid = await _smartAligner.AlignAsync(text, _currentAudioPath);
                    MainTier = smartGrid;
                    MainTier.Name = "Phrases (Locked)";
                    ValidateGrid();
                    StatusMessage = "Smart Alignment Complete.";
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText("smart_align_log.txt", $"[VM Critical] {ex}\n");
                    StatusMessage = $"Smart Align Failed: {ex.Message}. Using fallback.";
                    // Fallthrough to simple logic if VAD fails
                    LoadSimpleText(text);
                }
            }
            else
            {
                LoadSimpleText(text);
                StatusMessage = "Text imported (No Audio).";
            }

            CurrentTextGridPath = string.Empty;
            IsDirty = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error importing text: {ex.Message}";
        }
    }

    private void LoadSimpleText(string text)
    {
        text = Regex.Replace(text, @"\n{2,}", " _ ");
        text = text.Replace("\n", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        string cleanText = $"_ {text} _";

        var grid = new TextGrid();
        double duration = Timeline.TotalDuration > 0 ? Timeline.TotalDuration : 10.0;
        grid.AddInterval(0, duration, cleanText);
        MainTier = grid;
    }

    [RelayCommand]
    public async Task LoadTextGrid(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                StatusMessage = $"File not found: {path}";
                return;
            }

            StatusMessage = "Loading TextGrid...";
            var grid = await TextGridParser.ParseAsync(path);

            if (grid != null && grid.Intervals.Count > 0)
            {
                foreach (var boundary in grid.Boundaries)
                    boundary.IsLocked = true;

                MainTier = grid;
                ValidateGrid();
                CurrentTextGridPath = path;
                StatusMessage = $"Loaded TG: {Path.GetFileName(path)}";

                AppStateService.Current.LastTextGridPath = path;
                _ = AppStateService.SaveAsync();
            }
            else
            {
                CurrentTextGridPath = string.Empty;
                StatusMessage = "TG Load Failed (Empty/Error)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    [RelayCommand]
    public async Task SaveTextGrid()
    {
        if (MainTier == null)
        {
            StatusMessage = "No TextGrid to save.";
            return;
        }

        if (string.IsNullOrEmpty(CurrentTextGridPath))
        {
            StatusMessage = "Please use Save As first.";
            return;
        }

        try
        {
            await TextGridParser.SaveAsync(MainTier, CurrentTextGridPath);
            IsDirty = false;
            StatusMessage = "Saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save Failed: {ex.Message}";
        }
    }

    public async Task SaveTextGridAs(string path)
    {
        if (MainTier == null)
        {
            StatusMessage = "No TextGrid to save.";
            return;
        }

        try
        {
            await TextGridParser.SaveAsync(MainTier, path);
            CurrentTextGridPath = path;
            IsDirty = false;
            StatusMessage = $"Saved to {Path.GetFileName(path)}";

            AppStateService.Current.LastTextGridPath = path;
            _ = AppStateService.SaveAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save Failed: {ex.Message}";
        }
    }


    [RelayCommand]
    public async Task RunRecognition()
    {
        try
        {
            StatusMessage = "Running Inference...";

            List<string>? rawPhonemes = null;

            if (_nativeAligner.IsAvailable && HasOnnxModel && _currentAudioPath != null)
            {
                // Native ONNX path
                var result = await _nativeAligner.RecognizeAsync(_currentAudioPath);
                rawPhonemes = result;
            }

            if (rawPhonemes != null)
            {
                RecognitionPhonemes = rawPhonemes
                    .Select(p => p == "sil" ? "_" : p)
                    .ToList();
                StatusMessage = $"Recognition Complete ({RecognitionPhonemes.Count} frames)";
            }
            else
            {
                StatusMessage = "Recognition failed (No data).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    [RelayCommand]
    public async Task ConvertSelection(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            StatusMessage = "Select a phonemizer profile.";
            return;
        }

        if (MainTier == null)
        {
            StatusMessage = "No tier loaded.";
            return;
        }

        var targets = Timeline.IsSelectionActive
            ? MainTier.Intervals.Where(i =>
                i.End.Time > Timeline.SelectionStartTime &&
                i.Start.Time < Timeline.SelectionEndTime).ToList()
            : MainTier.Intervals.ToList();

        var validTargets = targets.Where(i => !string.IsNullOrWhiteSpace(i.Text)).ToList();

        if (!validTargets.Any())
        {
            StatusMessage = "No content to convert.";
            return;
        }

        StatusMessage = $"Converting {validTargets.Count} intervals...";

        var texts = validTargets.Select(i => i.Text).ToList();

        string cleanProfileName = StripCompatibilityIndicator(profileName);
        var profile = _phonemizerService.Profiles.Values
            .FirstOrDefault(p => p.DisplayName == cleanProfileName);

        if (profile == null)
        {
            StatusMessage = "Conversion Failed (Profile not found).";
            return;
        }

        var phonemized = await profile.PhonemizeListAsync(texts);
        if (phonemized == null || phonemized.Count != validTargets.Count)
        {
            StatusMessage = "Conversion Failed (Phonemizer error).";
            return;
        }

        var results = phonemized
            .Select(s => s?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList())
            .ToList();

        int changed = 0;
        for (int i = 0; i < validTargets.Count; i++)
        {
            var phonemes = results[i];
            if (phonemes != null && phonemes.Count > 0)
            {
                var displayPhonemes = phonemes.Select(p => p == "sil" ? "_" : p);
                validTargets[i].Text = string.Join(" ", displayPhonemes);
                validTargets[i].IsValid = true;
                changed++;
            }
        }

        StatusMessage = $"Converted {changed} intervals to IPA.";
        ValidateGrid();
    }

    [RelayCommand]
    public void ValidateGrid()
    {
        if (MainTier == null) return;

        // Use active model's phoneme set as the validator — 
        // completely decoupled from phonemizer dropdown selection
        var inventory = _phonemizerService.ActiveModel?.PhonemeSet;

        if (inventory == null || inventory.Count == 0)
        {
            StatusMessage = "Validation skipped: no model profile loaded. " +
                            "Ensure lua/models/rex_model.lua exists.";
            return;
        }

        int invalidCount = 0;
        foreach (var interval in MainTier.Intervals)
        {
            // Each interval text should be a single phoneme or
            // space-separated phonemes — split and check each token
            var tokens = interval.Text
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            bool allValid = tokens.Length > 0 &&
                            tokens.All(t => inventory.Contains(t));

            interval.IsValid = allValid;
            if (!allValid) invalidCount++;
        }

        StatusMessage = invalidCount == 0
            ? $"Validation passed — all {MainTier.Intervals.Count} intervals valid."
            : $"Validation: {invalidCount} invalid interval(s) found (shown in red).";
    }

    [RelayCommand]
    public async Task InsertSilenceToSelection()
    {
        if (MainTier == null)
        {
            StatusMessage = "No TextGrid loaded.";
            return;
        }

        if (!Timeline.IsSelectionActive)
        {
            StatusMessage = "No selection active.";
            return;
        }

        double selStart = Timeline.SelectionStartTime;
        double selEnd   = Timeline.SelectionEndTime;

        if (selEnd - selStart < 0.001)
        {
            StatusMessage = "Selection too short.";
            return;
        }

        // Guard: selection edges must not exactly overlap existing boundaries
        bool edgeCollision = MainTier.Boundaries.Any(b =>
            Math.Abs(b.Time - selStart) < 0.0001 ||
            Math.Abs(b.Time - selEnd)   < 0.0001);

        if (edgeCollision)
        {
            StatusMessage = "Selection edges overlap existing boundaries. Adjust selection.";
            return;
        }

        // Count interior boundaries strictly inside the selection
        var interiorBoundaries = MainTier.Boundaries
            .Where(b => b.Time > selStart && b.Time < selEnd)
            .ToList();

        // Case 0
        if (interiorBoundaries.Count > 1)
        {
            StatusMessage = "Cannot insert silence: multiple boundaries inside selection.";
            return;
        }

        bool isCase1 = interiorBoundaries.Count == 1;
        string leftText  = string.Empty;
        string rightText = string.Empty;

        if (isCase1)
        {
            // Texts are preserved automatically in TextGrid method — no dialog needed.
            // We just pass empty strings; the method ignores them for Case 1.
        }
        else
        {
            // Case 2: find containing interval
            var containing = MainTier.Intervals.FirstOrDefault(i =>
                i.Start.Time <= selStart && i.End.Time >= selEnd);

            if (containing == null)
            {
                StatusMessage = "Selection is not contained within a single interval.";
                return;
            }

            string originalText = containing.Text;
            if (string.IsNullOrWhiteSpace(originalText))
            {
                originalText = "_";
            }

            bool needsDialog = originalText != "_";

            if (needsDialog)
            {
                // Position dialog near selection start
                // (use a simple centered fallback since we have no control reference here)
                var dialog = new Frontend.Views.IntervalEditDialog(
                    originalText,
                    hintOverride: "Insert '---' to split text, then Enter");

                // Get main window as owner
                var desktop = Avalonia.Application.Current?.ApplicationLifetime
                    as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                var mainWindow = desktop?.MainWindow;

                if (mainWindow == null)
                {
                    StatusMessage = "Cannot open split dialog.";
                    return;
                }

                await dialog.ShowDialog(mainWindow);

                if (!dialog.IsConfirmed)
                    return; // user cancelled

                string result = dialog.ResultText ?? string.Empty;

                var match = System.Text.RegularExpressions.Regex.Match(result, @"-{3,}");
                if (match.Success)
                {
                    leftText = result.Substring(0, match.Index).Trim();
                    rightText = result.Substring(match.Index + match.Length).Trim();
                }
                else
                {
                    leftText = result.Trim();
                    rightText = "_";
                }

                // Ensure results are never empty
                if (string.IsNullOrWhiteSpace(leftText)) leftText = "_";
                if (string.IsNullOrWhiteSpace(rightText)) rightText = "_";

                // Ensure results are never empty
                if (string.IsNullOrWhiteSpace(leftText)) leftText = "_";
                if (string.IsNullOrWhiteSpace(rightText)) rightText = "_";
            }
            else
            {
                // Silence or empty: both sides keep the original text
                leftText = originalText;
                rightText = originalText;
            }
        }

        string? error = MainTier.InsertSilenceInterval(selStart, selEnd, leftText, rightText, isCase1);

        if (error != null)
        {
            StatusMessage = $"Insert Silence failed: {error}";
            return;
        }

        Timeline.ClearSelection();
        IsDirty = true;
        StatusMessage = "Silence interval inserted.";
    }

    [RelayCommand]
    public void FixSilences()
    {
        if (MainTier == null) return;

        var targets = Timeline.IsSelectionActive
            ? MainTier.Intervals.Where(i =>
                i.End.Time > Timeline.SelectionStartTime &&
                i.Start.Time < Timeline.SelectionEndTime).ToList()
            : MainTier.Intervals.ToList();

        if (targets.Count == 0) return;

        int changed = 0;

        foreach (var interval in targets)
        {
            string text = interval.Text ?? string.Empty;

            // Rule 1: blank → "_"
            if (string.IsNullOrWhiteSpace(text))
            {
                if (text != "_") { interval.Text = "_"; changed++; }
                continue;
            }

            // Already a silence marker — nothing to do
            if (text == "_")
                continue;

            // Rule 2: prepend/append "_" where missing — only for multi-phoneme/phrase text
            if (text.Length <= 4)
                continue;

            string updated = text;
            if (!updated.StartsWith("_ ") && updated != "_")
                updated = "_ " + updated;
            if (!updated.EndsWith(" _") && updated != "_")
                updated = updated + " _";

            if (updated != text) { interval.Text = updated; changed++; }
        }

        // Pass 2: merge consecutive "_" intervals (work on full grid, backwards)
        for (int i = MainTier.Intervals.Count - 1; i > 0; i--)
        {
            var cur = MainTier.Intervals[i];
            var prev = MainTier.Intervals[i - 1];

            if (cur.Text == "_" && prev.Text == "_")
            {
                // The shared boundary is the Start of cur (== End of prev)
                var sharedBoundary = cur.Start;
                if (MainTier.DeleteBoundary(sharedBoundary))
                {
                    changed++;
                }
            }
        }

        if (changed > 0)
        {
            IsDirty = true;
            StatusMessage = $"Fixed {changed} interval(s).";
            ValidateGrid();
        }
        else
        {
            StatusMessage = "Fix Silences: nothing to change.";
        }
    }

    [RelayCommand]
    public void ClearGrid()
    {
        if (Timeline.TotalDuration > 0)
        {
            LoadEmptyGrid(Timeline.TotalDuration, true);
            IsDirty = true;
            StatusMessage = "TextGrid cleared.";
        }
    }

    [RelayCommand]
    public void LockAllBoundaries()
    {
        if (MainTier == null) return;

        double startTime = Timeline.IsSelectionActive ? Timeline.SelectionStartTime : -0.001;
        double endTime = Timeline.IsSelectionActive ? Timeline.SelectionEndTime : double.MaxValue;

        bool changed = false;
        foreach (var boundary in MainTier.Boundaries)
        {
            if (boundary.Time >= startTime && boundary.Time <= endTime)
            {
                if (!boundary.IsLocked)
                {
                    boundary.IsLocked = true;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            RefreshTierUI();
            StatusMessage = Timeline.IsSelectionActive ? "Locked selected boundaries." : "Locked all boundaries.";
            IsDirty = true;
        }
    }

    [RelayCommand]
    public void LockSpaces()
    {
        if (MainTier == null) return;

        double startTime = Timeline.IsSelectionActive ? Timeline.SelectionStartTime : -0.001;
        double endTime = Timeline.IsSelectionActive ? Timeline.SelectionEndTime : double.MaxValue;
        bool changed = false;

        // Pass 1: lock boundaries that neighbor a space interval within range
        foreach (var interval in MainTier.Intervals)
        {
            if (interval.Text == "_")
            {
                if (interval.Start.Time >= startTime && interval.Start.Time <= endTime && !interval.Start.IsLocked)
                {
                    interval.Start.IsLocked = true;
                    changed = true;
                }
                if (interval.End.Time >= startTime && interval.End.Time <= endTime && !interval.End.IsLocked)
                {
                    interval.End.IsLocked = true;
                    changed = true;
                }
            }
        }

        // Pass 2: unlock all other boundaries within range
        foreach (var boundary in MainTier.Boundaries)
        {
            if (boundary.Time < startTime || boundary.Time > endTime) continue;

            // Skip absolute start/end
            if (boundary.Time <= 0.001 || (Timeline.TotalDuration > 0 && Math.Abs(boundary.Time - Timeline.TotalDuration) < 0.01))
                continue;

            bool isAdjacentToSpace = MainTier.Intervals.Any(i => i.Text == "_" && (i.Start == boundary || i.End == boundary));

            if (!isAdjacentToSpace && boundary.IsLocked)
            {
                boundary.IsLocked = false;
                changed = true;
            }
        }

        if (changed)
        {
            RefreshTierUI();
            StatusMessage = Timeline.IsSelectionActive ? "Locked spaces in selection." : "Locked boundaries adjacent to spaces.";
            IsDirty = true;
        }
    }

    [RelayCommand]
    public void UnlockAllBoundaries()
    {
        if (MainTier == null) return;

        double startTime = Timeline.IsSelectionActive ? Timeline.SelectionStartTime : -0.001;
        double endTime = Timeline.IsSelectionActive ? Timeline.SelectionEndTime : double.MaxValue;

        bool changed = false;
        foreach (var boundary in MainTier.Boundaries)
        {
            // Skip boundaries outside selection
            if (boundary.Time < startTime || boundary.Time > endTime) continue;

            // Protect absolute start and end boundaries
            if (boundary.Time <= 0.001 || (Timeline.TotalDuration > 0 && Math.Abs(boundary.Time - Timeline.TotalDuration) < 0.01))
            {
                if (!boundary.IsLocked)
                {
                    boundary.IsLocked = true;
                    changed = true;
                }
                continue;
            }

            if (boundary.IsLocked)
            {
                boundary.IsLocked = false;
                changed = true;
            }
        }

        if (changed)
        {
            RefreshTierUI();
            StatusMessage = Timeline.IsSelectionActive ? "Unlocked selected boundaries." : "Unlocked all boundaries.";
            IsDirty = true;
        }
    }

    private double _startPlaybackTime = 0;

    [RelayCommand]
    private void TogglePlayback()
    {
        if (AudioPlayer.IsPlaying)
        {
            AudioPlayer.Stop();
            Timeline.PlaybackPosition = _startPlaybackTime;
        }
        else
        {
            if (Timeline.IsSelectionActive)
            {
                _startPlaybackTime = Timeline.SelectionStartTime;
                Timeline.PlaybackPosition = _startPlaybackTime;
                AudioPlayer.Play(Timeline.SelectionStartTime, Timeline.SelectionEndTime);
            }
            else
            {
                _startPlaybackTime = Timeline.PlaybackPosition;
                if (_startPlaybackTime < 0 || _startPlaybackTime >= Timeline.TotalDuration)
                {
                    _startPlaybackTime = 0;
                }

                Timeline.PlaybackPosition = _startPlaybackTime;
                AudioPlayer.Play(_startPlaybackTime);
            }
        }
    }

    [RelayCommand]
    private void StopOrClear()
    {
        // Priority 1: close search overlay if it is currently visible.
        if (IsSearchVisible)
        {
            IsSearchVisible = false;
            return;
        }

        // Priority 2: stop playback when audio is running, otherwise clear selection.
        if (AudioPlayer.IsPlaying)
        {
            AudioPlayer.Stop();
            Timeline.PlaybackPosition = _startPlaybackTime;
        }
        else
        {
            Timeline.ClearSelection();
        }
    }

    [RelayCommand]
    public void ZoomToSelection()
    {
        if (Timeline.TotalDuration <= 0) return;

        // Mode 1: Zoom to Selection
        if (Timeline.IsSelectionActive)
        {
            double duration = Timeline.SelectionEndTime - Timeline.SelectionStartTime;
            
            if (duration > 0.01)
            {
                // 1. Zoom In
                Timeline.SetView(Timeline.SelectionStartTime, Timeline.SelectionEndTime);
                
                // 2. Clear the selection (Requested Feature)
                Timeline.ClearSelection();
                return;
            }
        }

        // Mode 2: Reset Zoom (Zoom All)
        Timeline.SetView(0, Timeline.TotalDuration);
    }

    [RelayCommand]
    private void PageForward()
    {
        Timeline.ScrollByRatio(0.85);
    }

    [RelayCommand]
    private void PageBack()
    {
        Timeline.ScrollByRatio(-0.85);
    }

    [RelayCommand]
    private void ZoomIn()
    {
        Timeline.Zoom(1.25);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        Timeline.Zoom(0.8);
    }

    [RelayCommand]
    private void GoToStart()
    {
        double duration = Timeline.VisibleEndTime - Timeline.VisibleStartTime;
        if (duration <= 0)
        {
            duration = Timeline.TotalDuration > 0 ? Timeline.TotalDuration : TimelineState.MinZoomDuration;
        }

        double endTime = Timeline.TotalDuration > 0
            ? Math.Min(duration, Timeline.TotalDuration)
            : duration;

        Timeline.PlaybackPosition = 0;
        Timeline.SetView(0, endTime);
    }

    [RelayCommand]
    private void GoToEnd()
    {
        if (Timeline.TotalDuration <= 0) return;

        double duration = Timeline.VisibleEndTime - Timeline.VisibleStartTime;
        if (duration <= 0)
        {
            duration = Math.Max(TimelineState.MinZoomDuration, Timeline.TotalDuration);
        }

        Timeline.PlaybackPosition = Timeline.TotalDuration;
        double newStart = Timeline.TotalDuration - duration;
        Timeline.SetView(newStart, Timeline.TotalDuration);
    }

    [RelayCommand]
    private void CenterCursor()
    {
        if (Timeline.PlaybackPosition < 0) return;

        Timeline.CenterOn(Timeline.PlaybackPosition);
    }

    [RelayCommand]
    private void PlayVisible()
    {
        double start = Timeline.VisibleStartTime;
        double end = Timeline.VisibleEndTime;

        if (end <= start) return;

        AudioPlayer.Play(start, end);
    }

    [RelayCommand]
    private void MoveCursorNext()
    {
        MoveCursorByOffset(1);
    }

    [RelayCommand]
    private void MoveCursorPrevious()
    {
        MoveCursorByOffset(-1);
    }

    private void MoveCursorByOffset(int direction)
    {
        if (direction == 0 || MainTier == null || MainTier.Intervals.Count == 0)
        {
            return;
        }

        var orderedIntervals = MainTier.Intervals.OrderBy(i => i.Start.Time).ToList();
        double cursor = Timeline.PlaybackPosition;
        if (cursor < 0) cursor = 0;

        int baseIndex = FindIntervalContainingOrNext(orderedIntervals, cursor);
        if (baseIndex == -1) return;

        int targetIndex = direction > 0 ? baseIndex + 1 : baseIndex - 1;
        if (targetIndex < 0 || targetIndex >= orderedIntervals.Count)
        {
            return;
        }

        double newTime = orderedIntervals[targetIndex].Start.Time;
        Timeline.PlaybackPosition = newTime;

        if (newTime < Timeline.VisibleStartTime || newTime > Timeline.VisibleEndTime)
        {
            Timeline.CenterOn(newTime);
        }
    }

    private static int FindIntervalContainingOrNext(List<TextInterval> intervals, double time)
    {
        if (intervals.Count == 0) return -1;

        int containing = intervals.FindIndex(i => time >= i.Start.Time && time < i.End.Time);
        if (containing >= 0) return containing;

        int next = intervals.FindIndex(i => i.Start.Time > time);
        if (next >= 0) return next;

        return intervals.Count - 1;
    }

    [RelayCommand]
    public void ToggleFileLogging()
    {
        if (AppLogger.HasSink<FileSink>())
        {
            AppLogger.Info("File logging stopped by user.");
            AppLogger.RemoveSink<FileSink>();
            IsFileLoggingActive = false;
            FileLoggingPath = string.Empty;
            StatusMessage = "File logging stopped.";
        }
        else
        {
            string logPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"VocalAligner_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            AppLogger.AddSink(new FileSink(logPath));
            AppLogger.Info("File logging started by user.");
            IsFileLoggingActive = true;
            FileLoggingPath = logPath;
            StatusMessage = $"Logging to: {Path.GetFileName(logPath)}";
        }
    }

    [RelayCommand]
    public void ShowSearch()
    {
        IsSearchVisible = true;
    }

    [RelayCommand]
    public void CloseSearch()
    {
        IsSearchVisible = false;
    }

    [RelayCommand]
    public void FindNext()
    {
        PerformSearch(true);
    }

    [RelayCommand]
    public void FindPrevious()
    {
        PerformSearch(false);
    }

    private void PerformSearch(bool forward)
    {
        if (MainTier == null || string.IsNullOrWhiteSpace(SearchQuery)) return;

        double cursorTime = Timeline.PlaybackPosition;
        if (cursorTime < 0) cursorTime = 0;

        var intervals = MainTier.Intervals.OrderBy(i => i.Start.Time).ToList();
        if (intervals.Count == 0) return;

        string queryRaw = SearchQuery.Trim();
        string[] tokens = queryRaw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        bool isMultiToken = tokens.Length > 1;

        StringComparison comparison = SearchCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        (bool IsMatch, int EndIndex) CheckMatchAt(int index)
        {
            if (index < 0 || index >= intervals.Count) return (false, -1);

            var currentInterval = intervals[index];
            string currentText = currentInterval.Text ?? string.Empty;

            bool singleMatch = SearchExactMatch
                ? currentText.Equals(queryRaw, comparison)
                : currentText.Contains(queryRaw, comparison);

            if (singleMatch) return (true, index);

            if (isMultiToken)
            {
                if (index + tokens.Length <= intervals.Count)
                {
                    bool seqMatch = true;
                    for (int t = 0; t < tokens.Length; t++)
                    {
                        var seqInterval = intervals[index + t];
                        string seqText = seqInterval.Text ?? string.Empty;

                        bool tokenMatch = SearchExactMatch
                            ? seqText.Equals(tokens[t], comparison)
                            : seqText.Contains(tokens[t], comparison);

                        if (!tokenMatch)
                        {
                            seqMatch = false;
                            break;
                        }
                    }

                    if (seqMatch) return (true, index + tokens.Length - 1);
                }
            }

            return (false, -1);
        }

        int startIndex = -1;

        if (forward)
        {
            startIndex = intervals.FindIndex(i => i.Start.Time >= cursorTime + 0.001);
            if (startIndex == -1) startIndex = 0;
        }
        else
        {
            startIndex = intervals.FindLastIndex(i => i.End.Time <= cursorTime - 0.001);
            if (startIndex == -1) startIndex = intervals.Count - 1;
        }

        int count = intervals.Count;
        int resultStartIdx = -1;
        int resultEndIdx = -1;

        for (int i = 0; i < count; i++)
        {
            int idx;
            if (forward)
            {
                idx = (startIndex + i) % count;
            }
            else
            {
                idx = (startIndex - i);
                if (idx < 0) idx += count;
            }

            var result = CheckMatchAt(idx);
            if (result.IsMatch)
            {
                resultStartIdx = idx;
                resultEndIdx = result.EndIndex;
                break;
            }
        }

        if (resultStartIdx != -1)
        {
            var firstMatch = intervals[resultStartIdx];
            var lastMatch = intervals[resultEndIdx];

            Timeline.PlaybackPosition = firstMatch.Start.Time;
            Timeline.SetSelection(firstMatch.Start.Time, lastMatch.End.Time);

            double matchStart = firstMatch.Start.Time;
            double matchEnd = lastMatch.End.Time;
            double matchDuration = matchEnd - matchStart;

            double currentZoomDuration = Timeline.VisibleEndTime - Timeline.VisibleStartTime;

            if (currentZoomDuration < matchDuration)
                currentZoomDuration = matchDuration * 1.5;

            if (currentZoomDuration < TimelineState.MinZoomDuration)
                currentZoomDuration = TimelineState.MinZoomDuration;

            double center = matchStart + (matchDuration / 2);
            double newStart = center - (currentZoomDuration / 2);
            double newEnd = newStart + currentZoomDuration;

            if (newStart < 0)
            {
                newStart = 0;
                newEnd = newStart + currentZoomDuration;
            }
            if (Timeline.TotalDuration > 0 && newEnd > Timeline.TotalDuration)
            {
                newEnd = Timeline.TotalDuration;
                newStart = newEnd - currentZoomDuration;
                if (newStart < 0) newStart = 0;
            }

            Timeline.SetView(newStart, newEnd);
        }
    }

    public void PlayRange(double start, double end)
    {
        if (AudioPlayer.IsPlaying)
        {
            AudioPlayer.Stop();
        }

        if (start < 0) start = 0;
        if (Timeline.TotalDuration > 0 && start >= Timeline.TotalDuration)
        {
            start = 0;
        }

        _startPlaybackTime = start;
        Timeline.PlaybackPosition = start;
        AudioPlayer.Play(start, end);
    }

    [RelayCommand]
    private void SeekTo(double time)
    {
        if (AudioPlayer.IsPlaying)
        {
            AudioPlayer.Stop();
        }

        if (time < 0) time = 0;
        if (Timeline.TotalDuration > 0 && time > Timeline.TotalDuration)
        {
            time = Timeline.TotalDuration;
        }

        _startPlaybackTime = time;
        Timeline.PlaybackPosition = time;
    }

    /// <summary>
    /// Realigns only the two intervals defined by [leftBoundaryIdx, leftBoundaryIdx+1, leftBoundaryIdx+2].
    /// Called after a smart drag operation. Does not replace MainTier.
    /// </summary>
    public async Task RealignScopedAsync(int leftBoundaryIdx, int rightBoundaryIdx)
    {
        if (MainTier == null || _currentAudioPath == null) return;

        // rightBoundaryIdx == leftBoundaryIdx + 2 always after MergeForSmartDrag
        int pivotIdx = leftBoundaryIdx + 1;
        int rightIdx = leftBoundaryIdx + 2;

        if (rightIdx >= MainTier.Boundaries.Count) return;
        if (pivotIdx >= MainTier.Intervals.Count) return;

        var leftInterval  = MainTier.Intervals[leftBoundaryIdx];
        var rightInterval = MainTier.Intervals[leftBoundaryIdx + 1];

        double startTime = MainTier.Boundaries[leftBoundaryIdx].Time;
        double pivotTime = MainTier.Boundaries[pivotIdx].Time;
        double endTime   = MainTier.Boundaries[rightIdx].Time;

        // --- Short-circuit: single-phoneme intervals need no alignment ---
        bool leftIsSingle  = !leftInterval.Text.Contains(' ');
        bool rightIsSingle = !rightInterval.Text.Contains(' ');

        if (leftIsSingle && rightIsSingle)
        {
            // Nothing to align — boundaries are already correct
            StatusMessage = "Smart drag complete (single phonemes, no realign needed).";
            return;
        }

        // --- Build token lists ---
        var leftTokens  = leftInterval.Text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var rightTokens = rightInterval.Text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        StatusMessage = "Scoped realign...";

        var interiorTimes = await _nativeAligner.AlignScopedAsync(
            _currentAudioPath,
            startTime, pivotTime, endTime,
            leftTokens, rightTokens);

        if (interiorTimes == null)
        {
            StatusMessage = "Scoped realign failed.";
            return;
        }

        // --- Write back interior boundaries in-place ---
        int expectedCount = (leftTokens.Count - 1) + (rightTokens.Count - 1);

        if (interiorTimes.Count != expectedCount)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[RealignScoped] Interior count mismatch: got {interiorTimes.Count}, expected {expectedCount}");
            StatusMessage = "Scoped realign failed (boundary count mismatch).";
            return;
        }

        _suppressDirtyNotifications = true;
        try
        {
            // Expand left interval back into individual phoneme intervals
            if (leftTokens.Count > 1)
            {
                var leftInteriorTimes = interiorTimes.Take(leftTokens.Count - 1).ToList();
                ExpandMergedInterval(leftBoundaryIdx, leftTokens, leftInteriorTimes);
            }

            // After expanding left, pivotIdx may have shifted — recalculate
            int newPivotIdx = leftBoundaryIdx + leftTokens.Count;

            // Expand right interval
            if (rightTokens.Count > 1)
            {
                var rightInteriorTimes = interiorTimes.Skip(leftTokens.Count - 1).ToList();
                ExpandMergedInterval(newPivotIdx, rightTokens, rightInteriorTimes);
            }
        }
        finally
        {
            _suppressDirtyNotifications = false;
        }

        IsDirty = true;
        StatusMessage = "Scoped realign complete.";
    }

    /// <summary>
    /// Takes a single merged interval at intervalIdx (with space-separated text)
    /// and splits it into individual single-phoneme intervals using the provided interior boundary times.
    /// interiorTimes.Count must equal tokens.Count - 1.
    /// </summary>
    private void ExpandMergedInterval(int intervalIdx, List<string> tokens, List<double> interiorTimes)
    {
        if (MainTier == null) return;
        if (tokens.Count <= 1) return; // nothing to expand

        var interval = MainTier.Intervals[intervalIdx];
        var endBoundary  = interval.End;

        // Set first interval to first token
        interval.Text = tokens[0];
        
        // Build new boundaries and intervals for tokens[1..]
        AlignmentBoundary prevBoundary = interval.Start;

        // Update first interval's end to first interior time
        var firstInteriorBoundary = new AlignmentBoundary
        {
            Time = interiorTimes[0],
            IsLocked = false
        };
        interval.End = firstInteriorBoundary;

        // Insert first interior boundary right after intervalIdx's start boundary
        int boundaryInsertPos = MainTier.Boundaries.IndexOf(interval.Start) + 1;
        MainTier.Boundaries.Insert(boundaryInsertPos, firstInteriorBoundary);

        prevBoundary = firstInteriorBoundary;

        // Insert remaining tokens
        for (int i = 1; i < tokens.Count; i++)
        {
            AlignmentBoundary nextBoundary;
            if (i == tokens.Count - 1)
            {
                // Last token: reuse the original end boundary
                nextBoundary = endBoundary;
            }
            else
            {
                nextBoundary = new AlignmentBoundary
                {
                    Time = interiorTimes[i],
                    IsLocked = false
                };
                MainTier.Boundaries.Insert(
                    MainTier.Boundaries.IndexOf(prevBoundary) + 1,
                    nextBoundary);
            }

            var newInterval = new TextInterval
            {
                Start = prevBoundary,
                End   = nextBoundary,
                Text  = tokens[i]
            };
            MainTier.Intervals.Insert(intervalIdx + i, newInterval);

            prevBoundary = nextBoundary;
        }
    }


    /// <summary>
    /// Manual-mode realignment: splits each interval's space-separated tokens
    /// into individual phoneme sub-intervals, then distributes them
    /// proportionally using PhonemeWeightCalculator.
    /// Respects locked boundaries as hard anchors.
    /// </summary>
    private void ElasticRealign()
    {
        if (MainTier == null) return;

        _suppressDirtyNotifications = true;
        try
        {
            // Pass 1: Expand all multi-token intervals into single-token intervals
            // Work backwards to avoid index shifting
            for (int i = MainTier.Intervals.Count - 1; i >= 0; i--)
            {
                var interval = MainTier.Intervals[i];
                var tokens = interval.Text
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length <= 1) continue; // already atomic

                // Determine span duration for placeholder distribution
                double spanStart = interval.Start.Time;
                double spanEnd   = interval.End.Time;
                double spanDur   = spanEnd - spanStart;

                // Compute weights for even placeholder placement
                double totalWeight = 0;
                var weights = new double[tokens.Length];
                for (int t = 0; t < tokens.Length; t++)
                {
                    weights[t] = Frontend.Services.Alignment.PhonemeWeightCalculator.GetWeight(tokens[t]);
                    totalWeight += weights[t];
                }
                if (totalWeight <= 0) totalWeight = tokens.Length;

                // Build new boundaries and intervals
                var origEnd = interval.End;
                interval.Text = tokens[0];

                double cursor = spanStart;
                AlignmentBoundary prevBoundary = interval.Start;

                for (int t = 0; t < tokens.Length; t++)
                {
                    double segDur = (weights[t] / totalWeight) * spanDur;
                    cursor += segDur;

                    AlignmentBoundary nextBoundary;
                    if (t == tokens.Length - 1)
                    {
                        nextBoundary = origEnd;
                    }
                    else
                    {
                        nextBoundary = new AlignmentBoundary
                        {
                            Time = cursor,
                            IsLocked = false
                        };
                        int insertPos = MainTier.Boundaries.IndexOf(prevBoundary) + 1;
                        MainTier.Boundaries.Insert(insertPos, nextBoundary);
                    }

                    if (t == 0)
                    {
                        interval.End = nextBoundary;
                    }
                    else
                    {
                        var newInterval = new TextInterval
                        {
                            Start = prevBoundary,
                            End   = nextBoundary,
                            Text  = tokens[t]
                        };
                        MainTier.Intervals.Insert(i + t, newInterval);
                    }

                    prevBoundary = nextBoundary;
                }
            }

            // Pass 2: Redistribute all spans between locked anchors
            Frontend.Services.Alignment.ElasticAligner.DistributeEntireGrid(MainTier);
        }
        finally
        {
            _suppressDirtyNotifications = false;
        }

        // Force TierControl to repaint by triggering MainTier property change
        var rebuilt = MainTier;
        MainTier = null;
        MainTier = rebuilt;

        IsDirty = true;
        StatusMessage = "Manual alignment complete.";
        ValidateGrid();
    }

    [RelayCommand]
    public async Task Realign()
    {
        // Guard: nothing to work with
        if (MainTier == null || MainTier.Intervals.Count == 0)
        {
            StatusMessage = "No TextGrid loaded.";
            return;
        }

        // Manual mode (elastic profile assigned, no ONNX)
        if (IsManualMode)
        {
            // Validate first — same rule as ONNX mode
            ValidateGrid();
            if (StatusMessage.Contains("invalid"))
            {
                StatusMessage += " Fix errors before aligning.";
                return;
            }

            // Split and distribute
            ElasticRealign();
            return;
        }

        // No profile assigned at all — distribute evenly (fallback, no validation)
        if (!IsModelLoaded)
        {
            Frontend.Services.Alignment.ElasticAligner.DistributeEntireGrid(MainTier);
            StatusMessage = "Elastic alignment complete (no profile).";
            IsDirty = true;
            return;
        }

        Console.WriteLine($"\n[VM.Realign] Called.");
        Console.WriteLine($"[VM.Realign] AudioPath={_currentAudioPath}");
        Console.WriteLine($"[VM.Realign] Intervals={MainTier?.Intervals.Count}, NativeAvailable={_nativeAligner.IsAvailable}");

        if (MainTier == null || MainTier.Intervals.Count == 0)
        {
            StatusMessage = "No TextGrid loaded.";
            return;
        }

        ValidateGrid();
        if (StatusMessage.Contains("invalid"))
        {
            StatusMessage += " Fix errors before aligning.";
            return;
        }

        var flatText = new List<string>();
        var constraints = new List<Models.AlignmentConstraint>();
        int phonemeCounter = 0;

        for (int i = 0; i < MainTier.Intervals.Count; i++)
        {
            var interval = MainTier.Intervals[i];

            if (!string.IsNullOrWhiteSpace(interval.Text))
            {
                var tokens = interval.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                flatText.AddRange(tokens);
                phonemeCounter += tokens.Length;

                if (interval.End.IsLocked)
                {
                    constraints.Add(new Models.AlignmentConstraint(interval.End.Time, phonemeCounter));
                }
            }
        }

        if (flatText.Count == 0)
        {
            StatusMessage = "Grid is empty.";
            return;
        }

        string fullText = string.Join(" ", flatText);

        if (!_nativeAligner.IsAvailable)
        {
            StatusMessage = "Error: Native model not found (rex_model.onnx).";
            return;
        }

        if (_currentAudioPath == null)
        {
            StatusMessage = "Error: No audio path.";
            return;
        }

        StatusMessage = constraints.Count > 0
            ? $"Aligning (Native ONNX, {constraints.Count} anchors)..."
            : "Aligning (Native ONNX)...";

        var response = await _nativeAligner.AlignAsync(_currentAudioPath, fullText, constraints);

        if (response == null || response.Intervals == null)
        {
            StatusMessage = "Alignment Failed.";
            return;
        }

        var mergedIntervals = new List<AlignmentInterval>();

        bool IsConstraintTime(double time)
        {
            // Check if this time corresponds to a hard constraint (approximate match)
            // Increased tolerance to 0.025s (25ms) to account for 20ms frame grid quantization
            return constraints.Any(c => Math.Abs(c.Time - time) < 0.025);
        }

        foreach (var apiInterval in response.Intervals)
        {
            string text = apiInterval.Text == "sil" ? "_" : apiInterval.Text;

            if (mergedIntervals.Count > 0)
            {
                var last = mergedIntervals.Last();

                if (last.Text == "_" && text == "_")
                {
                    if (!IsConstraintTime(last.End))
                    {
                        var newLast = new AlignmentInterval(last.Start, apiInterval.End, last.Text);
                        mergedIntervals[mergedIntervals.Count - 1] = newLast;
                        continue;
                    }
                }
            }

            mergedIntervals.Add(new AlignmentInterval(apiInterval.Start, apiInterval.End, text));
        }

        var newGrid = new TextGrid { Name = MainTier.Name };

        foreach (var item in mergedIntervals)
        {
            newGrid.AddInterval(item.Start, item.End, item.Text);
        }

        foreach (var boundary in newGrid.Boundaries)
        {
            if (boundary.Time <= 0.001 || Math.Abs(boundary.Time - Timeline.TotalDuration) < 0.01)
            {
                boundary.IsLocked = true;
            }
            else if (IsConstraintTime(boundary.Time))
            {
                boundary.IsLocked = true;
            }
        }

        MainTier = newGrid;
        ValidateGrid();
        StatusMessage = "Alignment Complete.";
        IsDirty = true;
    }

    private void SubscribeToGrid(TextGrid? grid)
    {
        if (_subscribedGrid != null)
        {
            _subscribedGrid.Intervals.CollectionChanged -= OnIntervalsCollectionChanged;
            _subscribedGrid.Boundaries.CollectionChanged -= OnBoundariesCollectionChanged;

            foreach (var interval in _trackedIntervals.ToList())
            {
                DetachInterval(interval);
            }

            foreach (var boundary in _trackedBoundaries.ToList())
            {
                DetachBoundary(boundary);
            }

            _trackedIntervals.Clear();
            _trackedBoundaries.Clear();
        }

        _subscribedGrid = grid;
        if (grid == null) return;

        _suppressDirtyNotifications = true;
        try
        {
            grid.Intervals.CollectionChanged += OnIntervalsCollectionChanged;
            grid.Boundaries.CollectionChanged += OnBoundariesCollectionChanged;

            foreach (var interval in grid.Intervals)
            {
                AttachInterval(interval);
            }

            foreach (var boundary in grid.Boundaries)
            {
                AttachBoundary(boundary);
            }
        }
        finally
        {
            _suppressDirtyNotifications = false;
        }
    }

    private void AttachInterval(TextInterval interval)
    {
        if (_trackedIntervals.Add(interval))
        {
            interval.PropertyChanged += OnIntervalPropertyChanged;
        }
    }

    private void DetachInterval(TextInterval interval)
    {
        if (_trackedIntervals.Remove(interval))
        {
            interval.PropertyChanged -= OnIntervalPropertyChanged;
        }
    }

    private void AttachBoundary(AlignmentBoundary boundary)
    {
        if (_trackedBoundaries.Add(boundary))
        {
            boundary.PropertyChanged += OnBoundaryPropertyChanged;
        }
    }

    private void DetachBoundary(AlignmentBoundary boundary)
    {
        if (_trackedBoundaries.Remove(boundary))
        {
            boundary.PropertyChanged -= OnBoundaryPropertyChanged;
        }
    }

    private void OnIntervalsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (TextInterval interval in e.OldItems)
            {
                DetachInterval(interval);
            }
        }

        if (e.NewItems != null)
        {
            foreach (TextInterval interval in e.NewItems)
            {
                AttachInterval(interval);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset && _subscribedGrid != null)
        {
            foreach (var interval in _trackedIntervals.ToList())
            {
                DetachInterval(interval);
            }

            foreach (var interval in _subscribedGrid.Intervals)
            {
                AttachInterval(interval);
            }
        }

        MarkDirty();
    }

    private void OnBoundariesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (AlignmentBoundary boundary in e.OldItems)
            {
                DetachBoundary(boundary);
            }
        }

        if (e.NewItems != null)
        {
            foreach (AlignmentBoundary boundary in e.NewItems)
            {
                AttachBoundary(boundary);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset && _subscribedGrid != null)
        {
            foreach (var boundary in _trackedBoundaries.ToList())
            {
                DetachBoundary(boundary);
            }

            foreach (var boundary in _subscribedGrid.Boundaries)
            {
                AttachBoundary(boundary);
            }
        }

        MarkDirty();
    }

    private void OnIntervalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextInterval.Text))
        {
            MarkDirty();
        }
    }

    private void OnBoundaryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AlignmentBoundary.Time) ||
            e.PropertyName == nameof(AlignmentBoundary.IsLocked))
        {
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (_suppressDirtyNotifications)
        {
            return;
        }

        IsDirty = true;
    }

    partial void OnMainTierChanged(TextGrid? value)
    {
        SubscribeToGrid(value);
        IsDirty = false;
    }

    partial void OnIsDirtyChanged(bool value)
    {
        // This will now use the updated WindowTitle logic above
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnSelectedModelChanged(IModelProfile? value)
    {
        _nativeAligner.UnloadModel();

        if (value != null)
            _phonemizerService.ActivateModel(value);
        else
            _phonemizerService.ClearActiveModel();

        RefreshPhonemizerDropdown();

        if (value != null)
            _ = _nativeAligner.LoadModelAsync(value);

        OnPropertyChanged(nameof(IsModelLoaded));
        OnPropertyChanged(nameof(HasOnnxModel));
        OnPropertyChanged(nameof(IsManualMode));

        if (!_suppressModelSave)
        {
            AppStateService.Current.SelectedModelId = value?.Id;
            AppStateService.Current.ModelSelectionSaved = true;
            _ = AppStateService.SaveAsync();
        }
    }

    [RelayCommand]
    public async Task LoadTextGridTier(string? path = null)
    {
        try
        {
            var desktop = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = desktop?.MainWindow;

            if (string.IsNullOrEmpty(path))
            {
                if (mainWindow == null) return;

                var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Open TextGrid File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("TextGrid Files") { Patterns = new[] { "*.TextGrid" } },
                        new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                    }
                });

                if (files.Count == 0) return;
                path = files[0].Path.LocalPath;
            }

            var tierList = await TextGridParser.ParseAllTiersAsync(path);
            if (tierList.Count == 0)
            {
                StatusMessage = "No tiers found or invalid TextGrid.";
                return;
            }

            TextGrid? selectedTier = null;
            if (tierList.Count == 1)
            {
                selectedTier = tierList[0];
            }
            else
            {
                if (mainWindow == null) return;
                var vm = new TierSelectionViewModel(tierList);
                var dialog = new Frontend.Views.TierSelectionDialog(vm);
                selectedTier = await dialog.ShowDialog<TextGrid?>(mainWindow) ?? null;
                if (selectedTier == null) return;
            }

            foreach (var boundary in selectedTier.Boundaries)
                boundary.IsLocked = true;

            MainTier = selectedTier;
            CurrentTextGridPath = path;
            ValidateGrid();
            StatusMessage = $"Loaded: {Path.GetFileName(path)} ({selectedTier.Name})";
            IsDirty = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }


    private void RefreshTierUI()
    {
        var currentGrid = MainTier;
        MainTier = null;
        MainTier = currentGrid;
    }

    private void LoadEmptyGrid(double duration, bool preserveCurrentPath = false)
    {
        var grid = new TextGrid();
        var start = new AlignmentBoundary { Time = 0, IsLocked = true };
        var end = new AlignmentBoundary { Time = Math.Max(duration, 0), IsLocked = true };

        grid.Boundaries.Add(start);
        grid.Boundaries.Add(end);

        grid.Intervals.Add(new TextInterval
        {
            Start = start,
            End = end,
            Text = string.Empty
        });

        MainTier = grid;

        if (!preserveCurrentPath)
        {
            CurrentTextGridPath = string.Empty;
        }
    }
}
