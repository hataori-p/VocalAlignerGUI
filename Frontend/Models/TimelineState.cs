using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Frontend.Models;

public partial class TimelineState : ObservableObject
{
    [ObservableProperty]
    private double _totalDuration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixelsPerSecond))]
    private double _visibleStartTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixelsPerSecond))]
    private double _visibleEndTime = 10.0; // Default to avoid 0 duration

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixelsPerSecond))]
    private double _canvasWidth;

    [ObservableProperty]
    private string _currentFileHash = string.Empty;

    [ObservableProperty]
    private double _playbackPosition = -1; // Default -1 means hidden/inactive

    [ObservableProperty]
    private double _guideLinePosition = -1; // Default -1 means hidden

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectionActive))]
    private double _selectionStartTime = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectionActive))]
    private double _selectionEndTime = -1;

    public bool IsSelectionActive => SelectionEndTime > SelectionStartTime && SelectionStartTime >= 0;

    [ObservableProperty]
    private bool _isInteracting;

    public double PixelsPerSecond => (CanvasWidth > 0 && (VisibleEndTime - VisibleStartTime) > 0)
        ? CanvasWidth / (VisibleEndTime - VisibleStartTime)
        : 0;

    public double TimeToX(double time)
    {
        var pps = PixelsPerSecond;
        if (pps == 0) return 0;
        return (time - VisibleStartTime) * pps;
    }

    public double XToTime(double x)
    {
        var pps = PixelsPerSecond;
        if (pps == 0) return VisibleStartTime;
        return VisibleStartTime + (x / pps);
    }

    public void ClearSelection()
    {
        SelectionStartTime = -1;
        SelectionEndTime = -1;
    }

    public void SetSelection(double start, double end)
    {
        if (start > end) (start, end) = (end, start); // Swap
        SelectionStartTime = Math.Max(0, start);
        SelectionEndTime = Math.Min(TotalDuration, end);
    }

    // Add this constraint constant
    public const double MinZoomDuration = 0.05; // 50ms

    /// <summary>
    /// Central method to update the view. Enforces bounds and minimum zoom.
    /// </summary>
    public void SetView(double start, double end)
    {
        // 1. Clamp to File Duration
        if (start < 0) start = 0;
        if (end > TotalDuration) end = TotalDuration;

        // 2. Enforce Minimum Zoom (prevent crash/empty spectrogram)
        if ((end - start) < MinZoomDuration)
        {
            double center = (start + end) / 2;
            start = center - (MinZoomDuration / 2);
            end = center + (MinZoomDuration / 2);

            // Re-clamp if centering pushed us out of bounds
            if (start < 0) { start = 0; end = MinZoomDuration; }
            if (end > TotalDuration) { end = TotalDuration; start = end - MinZoomDuration; }
        }

        VisibleStartTime = start;
        VisibleEndTime = end;
    }

    public void ScrollByRatio(double ratio)
    {
        double currentDuration = VisibleEndTime - VisibleStartTime;
        if (currentDuration <= 0)
        {
            currentDuration = Math.Max(MinZoomDuration, TotalDuration > 0 ? TotalDuration : MinZoomDuration);
        }

        double offset = currentDuration * ratio;
        double newStart = VisibleStartTime + offset;
        double newEnd = VisibleEndTime + offset;

        if (newStart < 0)
        {
            newStart = 0;
            newEnd = newStart + currentDuration;
        }
        else if (newEnd > TotalDuration)
        {
            newEnd = TotalDuration;
            newStart = newEnd - currentDuration;
        }

        if (newStart < 0) newStart = 0;
        if (newEnd > TotalDuration) newEnd = TotalDuration;

        SetView(newStart, newEnd);
    }

    public void Zoom(double factor)
    {
        double currentDuration = VisibleEndTime - VisibleStartTime;
        if (currentDuration <= 0)
        {
            currentDuration = Math.Max(MinZoomDuration, TotalDuration > 0 ? TotalDuration : MinZoomDuration);
        }

        double newDuration = currentDuration / factor;
        double center = (VisibleStartTime + VisibleEndTime) / 2.0;

        double newStart = center - (newDuration / 2.0);
        double newEnd = center + (newDuration / 2.0);

        SetView(newStart, newEnd);
    }

    public void CenterOn(double time)
    {
        double currentDuration = VisibleEndTime - VisibleStartTime;
        if (currentDuration <= 0)
        {
            currentDuration = Math.Max(MinZoomDuration, TotalDuration > 0 ? TotalDuration : MinZoomDuration);
        }

        double halfDuration = currentDuration / 2;
        SetView(time - halfDuration, time + halfDuration);
    }
}
