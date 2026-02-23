using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Frontend.Models;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Frontend.Controls;

public partial class CursorOverlay : UserControl
{
    // Pens
    private readonly IPen _playheadPen = new Pen(Brushes.Red, 1.5);
    // CHANGED: Use Yellow/Gold for the dragging guide as requested
    private readonly IPen _guideLinePen = new Pen(Brushes.Yellow, 2.0);

    // Darker line (Black with 50% opacity) to stand out against White spectrogram
    private readonly IPen _staticLinePen = new Pen(new SolidColorBrush(Color.Parse("#80000000")), 1);

    public CursorOverlay()
    {
        InitializeComponent();
        IsHitTestVisible = false; // Pass mouse events through
        ClipToBounds = true;
    }

    public static readonly StyledProperty<TimelineState?> TimelineProperty =
        AvaloniaProperty.Register<CursorOverlay, TimelineState?>(nameof(Timeline));

    public TimelineState? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<TextGrid?> DataSourceProperty =
        AvaloniaProperty.Register<CursorOverlay, TextGrid?>(nameof(DataSource));

    public TextGrid? DataSource
    {
        get => GetValue(DataSourceProperty);
        set => SetValue(DataSourceProperty, value);
    }

    public static readonly StyledProperty<double> TierHeightProperty =
        AvaloniaProperty.Register<CursorOverlay, double>(nameof(TierHeight), 100.0);

    public double TierHeight
    {
        get => GetValue(TierHeightProperty);
        set => SetValue(TierHeightProperty, value);
    }

    public static readonly StyledProperty<Control?> TierControlElementProperty =
        AvaloniaProperty.Register<CursorOverlay, Control?>(nameof(TierControlElement));

    public Control? TierControlElement
    {
        get => GetValue(TierControlElementProperty);
        set => SetValue(TierControlElementProperty, value);
    }

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
                InvalidateVisual(); // Initial draw
            }
        }

        if (change.Property == DataSourceProperty)
        {
            if (change.OldValue is TextGrid oldGrid)
            {
                oldGrid.Boundaries.CollectionChanged -= OnBoundariesChanged;
            }
            if (change.NewValue is TextGrid newGrid)
            {
                newGrid.Boundaries.CollectionChanged += OnBoundariesChanged;
            }
            InvalidateVisual();
        }

        if (change.Property == TierHeightProperty || change.Property == TierControlElementProperty)
        {
            InvalidateVisual();
        }
    }

    private void OnBoundariesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Redraw if any of these properties change
        if (e.PropertyName is nameof(TimelineState.PlaybackPosition)
                           or nameof(TimelineState.GuideLinePosition)
                           or nameof(TimelineState.VisibleStartTime)
                           or nameof(TimelineState.VisibleEndTime)
                           or nameof(TimelineState.CanvasWidth)
                           or nameof(TimelineState.IsSelectionActive)
                           or nameof(TimelineState.SelectionStartTime)
                           or nameof(TimelineState.SelectionEndTime))
        {
            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Timeline == null) return;

        double height = Bounds.Height;
        double width = Bounds.Width;

        var tierInfo = GetTierVisualInfo(height);

        // Layer 0: Selection Overlay
        if (Timeline.IsSelectionActive)
        {
            double x1 = Timeline.TimeToX(Timeline.SelectionStartTime);
            double x2 = Timeline.TimeToX(Timeline.SelectionEndTime);

            double visX1 = Math.Max(0, Math.Min(width, x1));
            double visX2 = Math.Max(0, Math.Min(width, x2));

            if (visX2 > visX1)
            {
                var selectionBrush = new SolidColorBrush(Color.Parse("#40007ACC"));
                context.FillRectangle(selectionBrush, new Rect(visX1, 0, visX2 - visX1, height));
            }
        }

        // Layer 1: Static Boundary Lines
        if (DataSource != null)
        {
            foreach (var boundary in DataSource.Boundaries)
            {
                double x = Timeline.TimeToX(boundary.Time);
                if (x >= 0 && x <= width)
                {
                    DrawCursorLine(context, _staticLinePen, x, height, tierInfo);
                }
            }
        }

        // Layer 2: Playhead
        // Only draw if active (>= 0) and within view
        if (Timeline.PlaybackPosition >= 0 &&
            Timeline.PlaybackPosition >= Timeline.VisibleStartTime &&
            Timeline.PlaybackPosition <= Timeline.VisibleEndTime)
        {
            double x = Timeline.TimeToX(Timeline.PlaybackPosition);
            DrawCursorLine(context, _playheadPen, x, height, tierInfo);
        }

        // Layer 3: Guide Line (drawn last to be on top)
        if (Timeline.GuideLinePosition >= 0)
        {
            double x = Timeline.TimeToX(Timeline.GuideLinePosition);
            if (x >= 0 && x <= width) // Only draw if visible
            {
                DrawCursorLine(context, _guideLinePen, x, height, tierInfo);
            }
        }
    }

    private (bool Exists, double TierTop, double TierBottom, double InteriorTop, double InteriorBottom) GetTierVisualInfo(double overlayHeight)
    {
        var tierControl = TierControlElement;

        if (tierControl != null && tierControl.Bounds.Height > 0)
        {
            var topLeft = tierControl.TranslatePoint(new Point(0, 0), this);
            var bottomLeft = tierControl.TranslatePoint(new Point(0, tierControl.Bounds.Height), this);

            if (topLeft.HasValue && bottomLeft.HasValue)
            {
                double tierTop = Math.Min(topLeft.Value.Y, bottomLeft.Value.Y);
                double tierBottom = Math.Max(topLeft.Value.Y, bottomLeft.Value.Y);

                var (localTop, localBottom) = TierControl.CalculateMarkerVerticalBounds(tierControl.Bounds.Height);

                double interiorTop = tierTop + localTop;
                double interiorBottom = tierTop + localBottom;

                return (
                    true,
                    Clamp(tierTop, 0, overlayHeight),
                    Clamp(tierBottom, 0, overlayHeight),
                    Clamp(interiorTop, 0, overlayHeight),
                    Clamp(interiorBottom, 0, overlayHeight)
                );
            }
        }

        double clampedHeight = Clamp(TierHeight, 0, overlayHeight);

        if (clampedHeight <= 0)
        {
            return (false, 0, 0, 0, 0);
        }

        double fallbackTop = overlayHeight - clampedHeight;
        double fallbackBottom = overlayHeight;

        var (fallbackLocalTop, fallbackLocalBottom) = TierControl.CalculateMarkerVerticalBounds(clampedHeight);

        double fallbackInteriorTop = Clamp(fallbackTop + fallbackLocalTop, 0, overlayHeight);
        double fallbackInteriorBottom = Clamp(fallbackTop + fallbackLocalBottom, 0, overlayHeight);

        return (true, Clamp(fallbackTop, 0, overlayHeight), Clamp(fallbackBottom, 0, overlayHeight), fallbackInteriorTop, fallbackInteriorBottom);
    }

    private void DrawCursorLine(DrawingContext context, IPen pen, double x, double overlayHeight, (bool Exists, double TierTop, double TierBottom, double InteriorTop, double InteriorBottom) tierInfo)
    {
        if (!tierInfo.Exists)
        {
            DrawSegment(context, pen, x, 0, overlayHeight, overlayHeight);
            return;
        }

        if (tierInfo.TierTop > 0)
        {
            DrawSegment(context, pen, x, 0, tierInfo.TierTop, overlayHeight);
        }

        DrawSegment(context, pen, x, tierInfo.InteriorTop, tierInfo.InteriorBottom, overlayHeight);

        if (tierInfo.TierBottom < overlayHeight)
        {
            DrawSegment(context, pen, x, tierInfo.TierBottom, overlayHeight, overlayHeight);
        }
    }

    private static void DrawSegment(DrawingContext context, IPen pen, double x, double y1, double y2, double overlayHeight)
    {
        double start = Clamp(Math.Min(y1, y2), 0, overlayHeight);
        double end = Clamp(Math.Max(y1, y2), 0, overlayHeight);

        if (double.IsNaN(start) || double.IsNaN(end) || end - start <= 0)
        {
            return;
        }

        context.DrawLine(pen, new Point(x, start), new Point(x, end));
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
