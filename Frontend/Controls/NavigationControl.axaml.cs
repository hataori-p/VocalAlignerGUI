using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using static Avalonia.Input.Gestures;
using Frontend.Models;
using Frontend.Services;
using System;
using System.ComponentModel;

namespace Frontend.Controls;

public partial class NavigationControl : UserControl
{
    private bool _isDragging;
    private double _clickStartX;

    // Visuals
    private readonly IPen _waveformPen = new Pen(Brushes.DarkGray, 1);
    private readonly IBrush _viewportBrush = new SolidColorBrush(Color.Parse("#40FFFF00")); // Semi-transparent Yellow
    private readonly IPen _viewportBorderPen = new Pen(Brushes.Goldenrod, 1);

    public NavigationControl()
    {
        InitializeComponent();
        Background = Brushes.Transparent;
        ClipToBounds = true;
        PointerWheelChanged += OnPointerWheelChanged;
    }

    #region Properties
    public static readonly StyledProperty<TimelineState?> TimelineProperty =
        AvaloniaProperty.Register<NavigationControl, TimelineState?>(nameof(Timeline));

    public TimelineState? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<AudioPlayerService?> PlayerServiceProperty =
        AvaloniaProperty.Register<NavigationControl, AudioPlayerService?>(nameof(PlayerService));

    public AudioPlayerService? PlayerService
    {
        get => GetValue(PlayerServiceProperty);
        set => SetValue(PlayerServiceProperty, value);
    }
    #endregion

    #region Lifecycle
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TimelineProperty)
        {
            if (change.OldValue is TimelineState oldT) oldT.PropertyChanged -= OnTimelineChanged;
            if (change.NewValue is TimelineState newT)
            {
                newT.PropertyChanged += OnTimelineChanged;
                InvalidateVisual();
            }
        }
        else if (change.Property == PlayerServiceProperty)
        {
            InvalidateVisual();
        }
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Redraw when timeline changes
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // If we resized, we should redraw.
        InvalidateVisual();
    }
    #endregion

    #region Rendering
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        
        double width = Bounds.Width;
        double height = Bounds.Height;
        var data = PlayerService?.WaveformData;

        // 1. Draw Waveform (Background)
        if (data != null && data.Length > 0 && width > 0)
        {
            double midY = height / 2;
            
            int samplesPerPixel = (int)(data.Length / width);
            if (samplesPerPixel < 1) samplesPerPixel = 1;

            for (int x = 0; x < width; x++)
            {
                int idx = x * samplesPerPixel;
                if (idx >= data.Length) break;

                float min = data[idx];
                float max = data[idx];

                for (int j = 1; j < samplesPerPixel; j++)
                {
                    if (idx + j >= data.Length) break;
                    float val = data[idx + j];
                    if (val < min) min = val;
                    if (val > max) max = val;
                }

                double y1 = midY - (max * midY);
                double y2 = midY - (min * midY);
                
                // Ensure we draw at least 1 pixel height
                if (Math.Abs(y2 - y1) < 1.0) y2 = y1 + 1;

                context.DrawLine(_waveformPen, new Point(x, y1), new Point(x, y2));
            }
        }

        // 2. Draw Viewport Rect (Highlight)
        if (Timeline != null && Timeline.TotalDuration > 0)
        {
            double scale = width / Timeline.TotalDuration;
            double x1 = Timeline.VisibleStartTime * scale;
            double x2 = Timeline.VisibleEndTime * scale;
            double w = x2 - x1;

            if (w < 1) w = 1; // Minimum visual width

            var rect = new Rect(x1, 0, w, height);
            context.FillRectangle(_viewportBrush, rect);
            context.DrawRectangle(_viewportBorderPen, rect);
        }
    }
    #endregion

    #region Interaction
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Timeline == null || Timeline.TotalDuration <= 0 || Bounds.Width <= 0) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            double delta = -e.Delta.Y;
            double ratio = delta * 0.1;
            Timeline.ScrollByRatio(ratio);
            e.Handled = true;
            return;
        }

        // 1. Calculate time under mouse on the Mini-Map
        double mouseX = e.GetPosition(this).X;
        double mouseRatio = mouseX / Bounds.Width;
        double timeAtMouse = mouseRatio * Timeline.TotalDuration;

        // 2. Calculate new duration
        double zoomFactor = 1.2;
        double currentDuration = Timeline.VisibleEndTime - Timeline.VisibleStartTime;
        double newDuration = e.Delta.Y > 0 ? currentDuration / zoomFactor : currentDuration * zoomFactor;

        // 3. Center the new view on the mouse position
        double halfDur = newDuration / 2;
        double newStart = timeAtMouse - halfDur;
        double newEnd = timeAtMouse + halfDur;

        Timeline.SetView(newStart, newEnd);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        if (point.X >= 0 && point.X <= Bounds.Width)
        {
            _isDragging = true;
            _clickStartX = point.X;
            // Interaction state is now delayed to OnPointerMoved
            MoveViewToPoint(point.X);
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging && Timeline != null)
        {
            var point = e.GetPosition(this);

            // THRESHOLD CHECK: Enable stable global scaling only after drag is confirmed
            if (!Timeline.IsInteracting)
            {
                if (Math.Abs(point.X - _clickStartX) > 3.0)
                {
                    Timeline.IsInteracting = true;
                }
            }

            MoveViewToPoint(point.X);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
        if (Timeline != null) Timeline.IsInteracting = false;
        e.Pointer.Capture(null);
    }

    private void MoveViewToPoint(double x)
    {
        if (Timeline == null || Timeline.TotalDuration <= 0) return;

        // Determine the time clicked
        double ratio = x / Bounds.Width;
        double clickTime = ratio * Timeline.TotalDuration;

        // Calculate current view duration to preserve zoom level
        double currentDuration = Timeline.VisibleEndTime - Timeline.VisibleStartTime;
        
        // Center the view on the clicked time
        double halfDur = currentDuration / 2;
        double newStart = clickTime - halfDur;
        double newEnd = clickTime + halfDur;

        // Use the centralized method to enforce bounds
        Timeline.SetView(newStart, newEnd);
    }
    #endregion
}
