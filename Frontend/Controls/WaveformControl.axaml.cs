using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using static Avalonia.Input.Gestures;
using Frontend.Models;
using Frontend.Services;
using Frontend.ViewModels;
using System;
using System.ComponentModel;

namespace Frontend.Controls;

public partial class WaveformControl : UserControl
{
    // State
    private bool _isDragging;
    private double _lastMouseX;
    private double _clickStartX;
    private float _globalMaxAmp = -1.0f;
    private double _selectionStartAnchor = -1;

    // Styling
    private readonly IPen _waveformPen = new Pen(Brushes.SlateBlue, 1);

    public WaveformControl()
    {
        InitializeComponent();
        PointerWheelChanged += OnPointerWheelChanged;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        
        // Important for custom rendering and hit testing
        ClipToBounds = true;
        Background = Brushes.Transparent;
    }

    #region Properties
    public static readonly StyledProperty<TimelineState?> TimelineProperty =
        AvaloniaProperty.Register<WaveformControl, TimelineState?>(nameof(Timeline));

    public TimelineState? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<AudioPlayerService?> PlayerServiceProperty =
        AvaloniaProperty.Register<WaveformControl, AudioPlayerService?>(nameof(PlayerService));

    public AudioPlayerService? PlayerService
    {
        get => GetValue(PlayerServiceProperty);
        set => SetValue(PlayerServiceProperty, value);
    }
    #endregion

    #region Lifecycle & Events
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TimelineProperty)
        {
            if (change.OldValue is TimelineState oldTimeline)
            {
                oldTimeline.PropertyChanged -= OnTimelineChanged;
            }
            if (change.NewValue is TimelineState newTimeline)
            {
                newTimeline.PropertyChanged += OnTimelineChanged;
                if (Bounds.Width > 0)
                {
                    newTimeline.CanvasWidth = Bounds.Width;
                }
                RefreshWaveform();
            }
        }
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimelineState.VisibleStartTime) 
                           or nameof(TimelineState.VisibleEndTime) 
                           or nameof(TimelineState.CanvasWidth)
                           or nameof(TimelineState.CurrentFileHash)
                           or nameof(TimelineState.IsInteracting))
        {
            RefreshWaveform();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Timeline != null)
        {
            Timeline.CanvasWidth = e.NewSize.Width;
        }
        else
        {
            RefreshWaveform();
        }
    }
    #endregion

    #region Data & Rendering
    private void RefreshWaveform()
    {
        _globalMaxAmp = -1.0f; // Recalculate on next render
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var data = PlayerService?.WaveformData;
        if (PlayerService is null || data is null || Timeline is null || Bounds.Width <= 0) return;

        double width = Bounds.Width;
        double height = Bounds.Height;
        double midY = height / 2;

        int startSample = (int)(Timeline.VisibleStartTime * PlayerService.SampleRate);
        int endSample = (int)(Timeline.VisibleEndTime * PlayerService.SampleRate);
        
        if (startSample < 0) startSample = 0;
        if (endSample > data.Length) endSample = data.Length;
        
        int totalSamples = endSample - startSample;
        if (totalSamples <= 0) return;

        double samplesPerPixel = (double)totalSamples / width;

        // --- Global Max Calculation (if needed) ---
        if (_globalMaxAmp < 0)
        {
            _globalMaxAmp = 0.01f; // Minimum floor
            // Full scan for global max. Stride for performance.
            for (int i = 0; i < data.Length; i += 10)
            {
                float val = Math.Abs(data[i]);
                if (val > _globalMaxAmp) _globalMaxAmp = val;
            }
        }

        // --- PASS 1: Find Local Max Amplitude ---
        float localMaxAmp;
        if (_isDragging || (Timeline?.IsInteracting ?? false))
        {
            // STABILIZATION: Use Global Max during drag to prevent flickering
            localMaxAmp = _globalMaxAmp;
        }
        else
        {
            localMaxAmp = 0.01f; // Minimum floor
            
            // Scan every pixel column to ensure we don't miss peaks
            // Optimization: If zoomed out (huge chunk), check a subset of samples per chunk
            int stride = Math.Max(1, (int)(samplesPerPixel / 10)); 

            for (int x = 0; x < width; x++)
            {
                int chunkStart = startSample + (int)(x * samplesPerPixel);
                int chunkEnd = startSample + (int)((x + 1) * samplesPerPixel);
                if (chunkStart >= data.Length) break;
                if (chunkEnd > data.Length) chunkEnd = data.Length;

                for (int i = chunkStart; i < chunkEnd; i += stride)
                {
                    float val = Math.Abs(data[i]);
                    if (val > localMaxAmp) localMaxAmp = val;
                }
            }
        }

        // Apply scaling factor (Zoom Y) with MARGIN
        // Target 90% height (0.9) to leave 5% gap at top/bottom
        double marginFactor = 0.9;
        double yScale = (1.0 / localMaxAmp) * marginFactor;

        // --- PASS 2: Draw ---
        for (int x = 0; x < width; x++)
        {
            int chunkStart = startSample + (int)(x * samplesPerPixel);
            int chunkEnd = startSample + (int)((x + 1) * samplesPerPixel);

            if (chunkStart >= data.Length) break;
            if (chunkEnd > data.Length) chunkEnd = data.Length;
            
            if (chunkEnd <= chunkStart) chunkEnd = chunkStart + 1;

            float min = data[chunkStart];
            float max = data[chunkStart];

            for (int i = chunkStart + 1; i < chunkEnd; i++)
            {
                float val = data[i];
                if (val < min) min = val;
                if (val > max) max = val;
            }

            // Apply Y-Scale
            double yMaxScaled = max * yScale;
            double yMinScaled = min * yScale;

            // Clamp visually to stay in box
            if (yMaxScaled > 1.0) yMaxScaled = 1.0;
            if (yMinScaled < -1.0) yMinScaled = -1.0;

            double y1 = midY - (yMaxScaled * midY);
            double y2 = midY - (yMinScaled * midY);

            y1 = Math.Max(0, Math.Min(height, y1));
            y2 = Math.Max(0, Math.Min(height, y2));

            if (Math.Abs(y2 - y1) < 1.0) y2 = y1 + 1;

            context.DrawLine(_waveformPen, new Point(x, y1), new Point(x, y2));
        }
    }
    #endregion

    #region Navigation
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Timeline is null || Bounds.Width <= 0) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            double delta = -e.Delta.Y;
            double ratio = delta * 0.1;
            Timeline.ScrollByRatio(ratio);
            e.Handled = true;
            return;
        }

        double zoomFactor = 1.2;
        double currentDuration = Timeline.VisibleEndTime - Timeline.VisibleStartTime;
        double newDuration = e.Delta.Y > 0 ? currentDuration / zoomFactor : currentDuration * zoomFactor;

        double mouseX = e.GetPosition(this).X;
        double mouseTimeRatio = mouseX / Bounds.Width;
        double timeAtMouse = Timeline.VisibleStartTime + (currentDuration * mouseTimeRatio);

        double newStartTime = timeAtMouse - (newDuration * mouseTimeRatio);
        double newEndTime = newStartTime + newDuration;

        Timeline.SetView(newStartTime, newEndTime);
    
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Timeline is null) return;

        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        
        double timeClicked = Timeline.XToTime(point.X);

        var vm = DataContext as MainWindowViewModel;
        bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || (vm?.IsVirtualShiftActive == true);

        if (properties.IsRightButtonPressed)
        {
            if (vm != null)
            {
                // Shift + Right Click = Play to End of File (-1)
                // Normal Right Click = Play to End of Viewport
                double endTime = isShift
                    ? -1
                    : (Timeline?.VisibleEndTime ?? -1);

                vm.PlayRange(timeClicked, endTime);
            }
            e.Handled = true;
            vm?.ConsumeOneShotModifiers();
            return;
        }

        if (properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastMouseX = point.X;
            _clickStartX = _lastMouseX;
            e.Pointer.Capture(this);
            e.Handled = true;

            if (isShift)
            {
                _selectionStartAnchor = timeClicked;
                Timeline.SetSelection(timeClicked, timeClicked);
            }
            else
            {
                _selectionStartAnchor = -1;
                // Clear selection deferred to OnPointerReleased so we can detect drags/double-clicks.
            }
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || Timeline is null) return;

        var timeline = Timeline;
        double currentX = e.GetPosition(this).X;
        double currentTime = timeline.XToTime(currentX);

        var vm = DataContext as MainWindowViewModel;
        bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || (vm?.IsVirtualShiftActive == true);

        if (isShift && _selectionStartAnchor >= 0)
        {
            timeline.SetSelection(_selectionStartAnchor, currentTime);
            e.Handled = true;
            return;
        }

        if (timeline.PixelsPerSecond <= 0) return;

        // THRESHOLD CHECK: Only start dragging behavior if moved > 3px
        if (!timeline.IsInteracting)
        {
            if (Math.Abs(currentX - _clickStartX) > 3.0)
            {
                timeline.IsInteracting = true;
            }
            else
            {
                return; // Ignore micro-movements
            }
        }

        double deltaX = currentX - _lastMouseX;
        double deltaSeconds = deltaX / timeline.PixelsPerSecond;

        double newStartTime = timeline.VisibleStartTime - deltaSeconds;
        double newEndTime = timeline.VisibleEndTime - deltaSeconds;

        timeline.SetView(newStartTime, newEndTime);

        _lastMouseX = currentX;
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _selectionStartAnchor = -1;
            if (Timeline != null) Timeline.IsInteracting = false;
            e.Pointer.Capture(null);

            var vm = DataContext as MainWindowViewModel;
            bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || (vm?.IsVirtualShiftActive == true);

            // Check if this was a Click (Seek) or a Drag (Pan)
            double currentX = e.GetPosition(this).X;

            if (Math.Abs(currentX - _clickStartX) < 3.0 && !isShift)
            {
                double time = Timeline?.XToTime(currentX) ?? 0;

                // Clear selection now that we know it's a valid single-click release.
                Timeline?.ClearSelection();

                if (vm != null)
                {
                    vm.SeekToCommand.Execute(time);
                }
            }

            vm?.ConsumeOneShotModifiers();

            e.Handled = true;
            InvalidateVisual(); // FORCE REPAINT
        }
    }
    #endregion
}
