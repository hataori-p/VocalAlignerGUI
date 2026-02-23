using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Frontend.Models;
using Frontend.ViewModels;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Frontend.Controls;

public class TierControl : Control
{
    // Pen thickness constants
    private const double BluePenThickness = 1.0;
    private const double RedPenThickness = 2.0;
    private const double HighlightPenThickness = 3.0;

    // Configuration
    private const double HoverThreshold = 12.0; // Increased from 5.0 for easier grabbing
    private const double MinIntervalDuration = 0.010; // 10 ms minimum duration
    private const double BorderThicknessPadding = 1.0;
    private const double MarkerSafetyPadding = 1.0;
    private const double AdditionalInteriorMargin = 0.0; // Extra inset to keep markers/cursor off the border

    // Pens & Brushes
    private readonly IPen BluePen = new Pen(Brushes.DeepSkyBlue, BluePenThickness);
    private readonly IPen RedPen = new Pen(Brushes.Red, RedPenThickness);
    private readonly IPen HighlightPen = new Pen(Brushes.Orange, HighlightPenThickness); // For hover
    private readonly IBrush TextBrush = Brushes.Black;
    private readonly Typeface Font = new Typeface(FontFamily.Default);

    // Interaction State
    private AlignmentBoundary? _hoverBoundary;
    private AlignmentBoundary? _dragBoundary;
    private bool _isDragging;
    private bool _isSmartDragging; // Alt+Drag state
    private int _smartDragLeftLimitIdx = -1;
    private int _smartDragRightLimitIdx = -1;
    private bool _isPlacingSplit; // Tracks the Ctrl+Drag split action
    private bool _isSelectingIntervals;
    private double _selectionAnchorStart = -1;
    private double _selectionAnchorEnd = -1;

    public TierControl()
    {
        // Enable Hit Testing
        Background = Brushes.Transparent;

        // Fix: Prevent markers from drawing outside the row bounds
        ClipToBounds = true;

        // Enable keyboard focus for paste handling
        Focusable = true;
    }

    #region Properties
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Panel.BackgroundProperty.AddOwner<TierControl>();

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public static readonly StyledProperty<TimelineState?> TimelineProperty =
        AvaloniaProperty.Register<TierControl, TimelineState?>(nameof(Timeline));

    public TimelineState? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<TextGrid?> DataSourceProperty =
        AvaloniaProperty.Register<TierControl, TextGrid?>(nameof(DataSource));

    public TextGrid? DataSource
    {
        get => GetValue(DataSourceProperty);
        set => SetValue(DataSourceProperty, value);
    }
    #endregion

    #region Lifecycle & Events
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TimelineProperty)
        {
            if (change.OldValue is TimelineState oldT) oldT.PropertyChanged -= OnStateChanged;
            if (change.NewValue is TimelineState newT)
            {
                newT.PropertyChanged += OnStateChanged;
                if (Bounds.Width > 0) newT.CanvasWidth = Bounds.Width;
            }
            InvalidateVisual();
        }

        if (change.Property == DataSourceProperty)
        {
            OnDataSourceChanged(change.OldValue as TextGrid, change.NewValue as TextGrid);
        }
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimelineState.VisibleStartTime)
            or nameof(TimelineState.VisibleEndTime)
            or nameof(TimelineState.CanvasWidth))
        {
            InvalidateVisual();
        }
    }

    // Keep TextGrid subscriptions current so UI reflects edits immediately.
    private void OnDataSourceChanged(TextGrid? oldGrid, TextGrid? newGrid)
    {
        if (oldGrid != null)
        {
            oldGrid.Boundaries.CollectionChanged -= OnBoundariesCollectionChanged;
            oldGrid.Intervals.CollectionChanged -= OnIntervalsCollectionChanged;

            foreach (var item in oldGrid.Intervals)
                item.PropertyChanged -= OnIntervalPropertyChanged;
        }

        if (newGrid != null)
        {
            newGrid.Boundaries.CollectionChanged += OnBoundariesCollectionChanged;
            newGrid.Intervals.CollectionChanged += OnIntervalsCollectionChanged;

            foreach (var item in newGrid.Intervals)
                item.PropertyChanged += OnIntervalPropertyChanged;
        }
        InvalidateVisual();
    }

    private void OnBoundariesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnIntervalsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (TextInterval item in e.OldItems)
                item.PropertyChanged -= OnIntervalPropertyChanged;
        }
        if (e.NewItems != null)
        {
            foreach (TextInterval item in e.NewItems)
                item.PropertyChanged += OnIntervalPropertyChanged;
        }
        InvalidateVisual();
    }

    private void OnIntervalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextInterval.Text) || e.PropertyName == nameof(TextInterval.IsValid))
        {
            InvalidateVisual();
        }
    }
    #endregion

    #region Rendering
    public override void Render(DrawingContext context)
    {
        // --- CRITICAL FIX: HIT TESTING ---
        // We must draw a transparent rectangle over the full bounds.
        // This ensures the "empty" space between markers captures mouse events
        // instead of letting them fall through to the parent container.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        if (Timeline == null || DataSource == null) return;

        double width = Bounds.Width;
        double height = Bounds.Height;

        var (markerTop, markerBottom) = CalculateMarkerVerticalBounds(height);
        if (markerBottom <= markerTop) return;

        // 1. Draw Intervals
        foreach (var interval in DataSource.Intervals)
        {
            double x1 = Timeline.TimeToX(interval.Start.Time);
            double x2 = Timeline.TimeToX(interval.End.Time);

            // Optimization: Skip if completely off-screen
            if (x2 < 0 || x1 > width) continue;

            if (!string.IsNullOrEmpty(interval.Text))
            {
                // --- FLOATING LABEL LOGIC ---
                // Calculate the visible portion of the interval
                double visibleX1 = Math.Max(0, x1);
                double visibleX2 = Math.Min(width, x2);
                double visibleWidth = visibleX2 - visibleX1;

                // Only draw text if the visible slice is wide enough to be useful
                if (visibleWidth > 5)
                {
                    var brush = interval.IsValid ? TextBrush : Brushes.Red;

                    var ft = new FormattedText(
                        interval.Text,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        Font,
                        12,
                        brush
                    );

                    // Constrain text width to the VISIBLE portion (minus padding)
                    double maxTextWidth = Math.Max(0, visibleWidth - 4);
                    if (ft.Width > maxTextWidth)
                    {
                        ft.MaxTextWidth = maxTextWidth;
                        ft.Trimming = TextTrimming.CharacterEllipsis; // Add dots if too long
                    }

                    // Center text within the VISIBLE portion
                    double textX = visibleX1 + (visibleWidth / 2) - (ft.Width / 2);
                    double textY = (height / 2) - (ft.Height / 2);

                    context.DrawText(ft, new Point(textX, textY));
                }
            }
        }

        // 2. Draw Boundaries
        foreach (var b in DataSource.Boundaries)
        {
            // Skip the start (Red Line fix)
            if (b.Time <= 0.0001) continue;

            double x = Timeline.TimeToX(b.Time);
            if (x < -2 || x > width + 2) continue;

            IPen pen;
            if (b == _dragBoundary || b == _hoverBoundary) pen = HighlightPen;
            else pen = b.IsLocked ? RedPen : BluePen;

            context.DrawLine(pen, new Point(x, markerTop), new Point(x, markerBottom));
        }
    }
    #endregion

    #region Mouse Interaction

    private void OpenContextMenu(AlignmentBoundary boundary, TextInterval? interval, Point mousePos)
    {
        _ = mousePos; // Reserved for future positioning logic

        var menu = new ContextMenu();
        var ds = DataSource;
        if (ds == null) return;

        // 1. Rename
        if (interval != null)
        {
            var renameItem = new MenuItem { Header = "Rename Interval..." };
            renameItem.Click += (s, e) => ShowEditDialog(interval);
            menu.Items.Add(renameItem);
        }

        // 2. Lock/Unlock
        var lockItem = new MenuItem
        {
            Header = boundary.IsLocked ? "Unlock Anchor" : "Lock Anchor"
        };
        lockItem.Click += (s, e) =>
        {
            boundary.IsLocked = !boundary.IsLocked;
            InvalidateVisual();
        };
        menu.Items.Add(lockItem);

        menu.Items.Add(new Separator());

        // 3. Delete
        // Check constraints: Cannot delete First or Last boundary
        bool isDeletable = ds.Boundaries.IndexOf(boundary) > 0 && ds.Boundaries.IndexOf(boundary) < ds.Boundaries.Count - 1;

        var deleteItem = new MenuItem { Header = "Delete Boundary (Merge)" };
        deleteItem.IsEnabled = isDeletable;
        deleteItem.InputGesture = new KeyGesture(Key.Delete);
        deleteItem.Click += (s, e) =>
        {
            if (ds.DeleteBoundary(boundary))
            {
                InvalidateVisual();
            }
        };
        menu.Items.Add(deleteItem);

        // --- FIX: DO NOT assign to 'ContextMenu'; open transient menu manually ---
        menu.Open(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Timeline == null || DataSource == null) return;

        var point = e.GetPosition(this);
        double currentMouseTime = Timeline.XToTime(point.X);

        // --- SPLIT PLACEMENT LOGIC (Ctrl+Drag) ---
        if (_isPlacingSplit)
        {
            // Update the global guide line to follow mouse
            Timeline.GuideLinePosition = currentMouseTime;
            return;
        }

        if (_isSelectingIntervals)
        {
            var interval = DataSource.Intervals.FirstOrDefault(i => i.Start.Time <= currentMouseTime && i.End.Time > currentMouseTime);
            double targetStart = interval?.Start.Time ?? currentMouseTime;
            double targetEnd = interval?.End.Time ?? currentMouseTime;

            double anchorStart = _selectionAnchorStart >= 0 ? _selectionAnchorStart : targetStart;
            double anchorEnd = _selectionAnchorEnd >= 0 ? _selectionAnchorEnd : targetEnd;

            Timeline.SetSelection(Math.Min(anchorStart, targetStart), Math.Max(anchorEnd, targetEnd));
            e.Handled = true;
            return;
        }

        // --- DRAGGING LOGIC (Existing Boundary) ---
        if (_isDragging && _dragBoundary != null)
        {
            double minLimit = 0;
            double maxLimit = Timeline.TotalDuration;
            int currentIndex = DataSource.Boundaries.IndexOf(_dragBoundary);

            if (_isSmartDragging)
            {
                if (_smartDragLeftLimitIdx >= 0 && _smartDragLeftLimitIdx < DataSource.Boundaries.Count)
                {
                    var prev = DataSource.Boundaries[_smartDragLeftLimitIdx];
                    minLimit = prev.Time + MinIntervalDuration;
                }

                if (_smartDragRightLimitIdx >= 0 && _smartDragRightLimitIdx < DataSource.Boundaries.Count)
                {
                    var next = DataSource.Boundaries[_smartDragRightLimitIdx];
                    maxLimit = next.Time - MinIntervalDuration;
                }
            }
            else
            {
                if (currentIndex > 0)
                {
                    var prev = DataSource.Boundaries[currentIndex - 1];
                    minLimit = prev.Time + MinIntervalDuration;
                }

                if (currentIndex < DataSource.Boundaries.Count - 1)
                {
                    var next = DataSource.Boundaries[currentIndex + 1];
                    maxLimit = next.Time - MinIntervalDuration;
                }
            }

            double newTime = Math.Max(minLimit, Math.Min(currentMouseTime, maxLimit));

            _dragBoundary.Time = newTime;
            _dragBoundary.IsLocked = true; // Always lock on move

            // Elastic redistribution (no-model mode)
            if (DataContext is MainWindowViewModel elasticVmMove && !elasticVmMove.HasOnnxModel
                && _isSmartDragging
                && _smartDragLeftLimitIdx >= 0 && _smartDragRightLimitIdx >= 0)
            {
                int pivotIdx = DataSource.Boundaries.IndexOf(_dragBoundary);
                if (pivotIdx > 0)
                {
                    Frontend.Services.Alignment.ElasticAligner.DistributeWithPivot(
                        DataSource,
                        _smartDragLeftLimitIdx,
                        pivotIdx,
                        _smartDragRightLimitIdx);
                }
            }

            if (Timeline != null) Timeline.GuideLinePosition = newTime;

            InvalidateVisual();
            return;
        }

        // --- HOVER LOGIC ---
        double pxThreshold = HoverThreshold;
        double timeThreshold = pxThreshold / Timeline.PixelsPerSecond;

        // Find closest boundary
        var closest = DataSource.Boundaries
            .Where(b => Math.Abs(b.Time - currentMouseTime) < timeThreshold)
            .OrderBy(b => Math.Abs(b.Time - currentMouseTime))
            .FirstOrDefault();

        if (closest != _hoverBoundary)
        {
            _hoverBoundary = closest;
            InvalidateVisual();
        }

        // Update Cursor
        Cursor = _hoverBoundary != null ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (Timeline == null || DataSource == null) return;

            var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null) return;

            string? text = await ClipboardExtensions.TryGetTextAsync(topLevel.Clipboard);
            if (string.IsNullOrWhiteSpace(text)) return;

            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            text = Regex.Replace(text, @"\n{2,}", " _ ");
            text = text.Replace("\n", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (DataSource.Intervals.Count <= 1)
            {
                text = $"_ {text} _";
            }

            TextInterval? target = null;

            if (Timeline.IsSelectionActive)
            {
                target = DataSource.Intervals.FirstOrDefault(i =>
                    i.Start.Time < Timeline.SelectionEndTime &&
                    i.End.Time > Timeline.SelectionStartTime);
            }
            else
            {
                double pos = Timeline.PlaybackPosition >= 0 ? Timeline.PlaybackPosition : 0;
                target = DataSource.Intervals.FirstOrDefault(i => i.Start.Time <= pos && i.End.Time > pos);
            }

            if (target != null)
            {
                target.Text = text;
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }

    private async void ShowEditDialog(TextInterval interval)
    {
        var visualRoot = Avalonia.VisualTree.VisualExtensions.GetVisualRoot(this) as Window;
        var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this);
        var timeline = Timeline;

        if (visualRoot != null && topLevel != null && timeline != null)
        {
            double xPos = timeline.TimeToX(interval.Start.Time);
            if (xPos < 0) xPos = 0;

            var screenPoint = this.PointToScreen(new Point(xPos, 0));
            var screens = topLevel.Screens;
            var screen = screens?.ScreenFromPoint(screenPoint) ?? screens?.Primary;

            // Estimated dimensions (Initial height for placement logic)
            // We assume a conservative height to prevent clipping before the dialog self-corrects
            double estimatedDlgHeight = 300;
            double dlgWidth = 280;

            double targetX = screenPoint.X;
            double targetY = screenPoint.Y + Bounds.Height; // Default: Place Below Tier

            if (screen != null)
            {
                var workingArea = screen.WorkingArea;

                // 1. Check vertical fit below
                if (targetY + estimatedDlgHeight > workingArea.Bottom)
                {
                    // Not enough space below, place ABOVE the tier
                    // Offset by the estimated height so the bottom of the dialog touches the top of the tier
                    targetY = screenPoint.Y - estimatedDlgHeight;
                }

                // 2. Check horizontal fit (clamp to screen edges)
                if (targetX + dlgWidth > workingArea.Right)
                    targetX = workingArea.Right - dlgWidth;
                if (targetX < workingArea.X)
                    targetX = workingArea.X;

                // 3. Final vertical clamp check
                if (targetY < workingArea.Y)
                    targetY = workingArea.Y;
            }

            // Create and Show Dialog
            // Note: The Dialog's own 'EnsureOnScreen' logic will handle fine-tuning
            var dialog = new Frontend.Views.IntervalEditDialog(interval.Text);
            dialog.Position = new PixelPoint((int)targetX, (int)targetY);

            // Pass the Main Window as owner so it behaves like a tool window (not Topmost, but owned)
            await dialog.ShowDialog(visualRoot);

            if (dialog.IsConfirmed)
            {
                interval.Text = dialog.ResultText;

                // Re-run the existing validation logic from the ViewModel
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.ValidateGridCommand.Execute(null);
                }

                InvalidateVisual(); // Redraw with new text and updated IsValid color
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus(); // Take focus so keyboard shortcuts work
        base.OnPointerPressed(e);

        var props = e.GetCurrentPoint(this).Properties;
        var point = e.GetPosition(this);
        var timeline = Timeline;
        var dataSource = DataSource;

        if (timeline == null || dataSource == null) return;

        double timeClicked = timeline.XToTime(point.X);

        // ---------------------------------------------------------
        // 1. Handle RIGHT CLICK (Context Menu / Play Interval / Delete Boundary)
        // ---------------------------------------------------------
        if (props.IsRightButtonPressed)
        {
            e.Handled = true;
            // Ensure no stale menu remains attached between interactions.
            ContextMenu = null;
            bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            // A. Priority: Check for Boundary Hit (Quick Delete)
            // Re-check hover logic to ensure we are strictly on the line
            AlignmentBoundary? targetBoundary = _hoverBoundary; 
            
            if (isCtrl && targetBoundary != null)
            {
                // ACTION: Delete Boundary
                if (dataSource.DeleteBoundary(targetBoundary))
                {
                    InvalidateVisual();
                }
                return;
            }

            // B. Interval Hit
            var interval = dataSource.Intervals.FirstOrDefault(i => i.Start.Time <= timeClicked && i.End.Time > timeClicked);
            if (interval != null)
            {
                if (isCtrl)
                {
                    // ACTION: Context Menu (Ctrl + Right Click on Interval)
                    // Pass null for boundary so menu knows it's interval-focused
                    OpenContextMenu(interval.Start, interval, point); 
                }
                else
                {
                    // ACTION: Play Interval (Right Click on Interval)
                    if (DataContext is MainWindowViewModel vm)
                    {
                        vm.PlayRange(interval.Start.Time, interval.End.Time);
                    }
                }
            }
            return;
        }

        // ---------------------------------------------------------
        // 2. Handle Interval Selection (Shift + Left Click)
        // ---------------------------------------------------------
        if (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var interval = dataSource.Intervals.FirstOrDefault(i => i.Start.Time <= timeClicked && i.End.Time > timeClicked);

            _isSelectingIntervals = true;
            if (interval != null)
            {
                _selectionAnchorStart = interval.Start.Time;
                _selectionAnchorEnd = interval.End.Time;
                timeline.SetSelection(interval.Start.Time, interval.End.Time);
            }
            else
            {
                _selectionAnchorStart = timeClicked;
                _selectionAnchorEnd = timeClicked;
                timeline.SetSelection(timeClicked, timeClicked);
            }

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // ---------------------------------------------------------
        // 3. Handle SPLIT START (Ctrl + Left Click Down)
        // ---------------------------------------------------------
        if (props.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (_hoverBoundary == null)
            {
                // Start the "Placement Mode"
                _isPlacingSplit = true;
                e.Pointer.Capture(this);
                
                // Show the global yellow guide at current mouse position
                timeline.GuideLinePosition = timeClicked;
                
                e.Handled = true;
            }
            return;
        }

        // ---------------------------------------------------------
        // 3.5 Handle Boundary Lock Toggle (Double Click on Boundary)
        // ---------------------------------------------------------
        if (e.ClickCount == 2 && props.IsLeftButtonPressed && _hoverBoundary != null)
        {
            _hoverBoundary.IsLocked = !_hoverBoundary.IsLocked;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ---------------------------------------------------------
        // 4. Handle Text Editing (Double Click on Interval)
        // ---------------------------------------------------------
        if (e.ClickCount == 2 && props.IsLeftButtonPressed)
        {
            var interval = dataSource.Intervals.FirstOrDefault(i => i.Start.Time <= timeClicked && i.End.Time > timeClicked);
            if (interval != null)
            {
                e.Handled = true;
                ShowEditDialog(interval);
            }
            return;
        }

        // ---------------------------------------------------------
        // 5. Handle Interval Click (Move Cursor)
        // ---------------------------------------------------------
        if (props.IsLeftButtonPressed && _hoverBoundary == null && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var interval = dataSource.Intervals.FirstOrDefault(i => i.Start.Time <= timeClicked && i.End.Time > timeClicked);
            if (interval != null && DataContext is MainWindowViewModel vm)
            {
                timeline.ClearSelection();
                vm.SeekToCommand.Execute(interval.Start.Time);
                e.Handled = true;
                return;
            }
        }

        // ---------------------------------------------------------
        // 6. Handle Dragging (Left Click + Hovering Boundary)
        // ---------------------------------------------------------
        if (props.IsLeftButtonPressed && _hoverBoundary != null)
        {
            // ── Elastic mode (no ONNX model loaded) ──────────────────────────────────
            if (DataContext is MainWindowViewModel elasticVmPress && !elasticVmPress.HasOnnxModel)
            {
                int idx = dataSource.Boundaries.IndexOf(_hoverBoundary);
                if (idx > 0 && idx < dataSource.Boundaries.Count - 1)
                {
                    var (left, right) = dataSource.FindSurroundingAnchors(idx);
                    _smartDragLeftLimitIdx  = left;
                    _smartDragRightLimitIdx = right;
                    _isSmartDragging        = true;
                }
                _isDragging   = true;
                _dragBoundary = _hoverBoundary;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                _isSmartDragging = false;
                _smartDragLeftLimitIdx = -1;
                _smartDragRightLimitIdx = -1;

                int idx = dataSource.Boundaries.IndexOf(_hoverBoundary);

                if (idx >= 0)
                {
                    var (left, right) = dataSource.FindSurroundingAnchors(idx);

                    // VALIDATION CHECK: Ensure all intervals in this range are valid IPA.
                    // If not, we cannot use Smart Drag because Realign will fail,
                    // leaving the user with merged/destroyed intervals.
                    bool rangeIsValid = true;

                    // Interval indices correspond to Boundary indices.
                    // Interval[i] is between Boundary[i] and Boundary[i+1].
                    // We check intervals from 'left' to 'right-1'.
                    for (int i = left; i < right; i++)
                    {
                        if (i >= 0 && i < dataSource.Intervals.Count)
                        {
                            if (!dataSource.Intervals[i].IsValid)
                            {
                                rangeIsValid = false;
                                break;
                            }
                        }
                    }

                    if (rangeIsValid && right - left >= 2)
                    {
                        dataSource.MergeForSmartDrag(left, idx, right);

                        _isSmartDragging = true;
                        _smartDragLeftLimitIdx = left;
                        _smartDragRightLimitIdx = left + 2;
                    }
                    // Else: Fallthrough to normal drag (handled below)
                }
            }
            else
            {
                _isSmartDragging = false;
                _smartDragLeftLimitIdx = -1;
                _smartDragRightLimitIdx = -1;
            }

            _isDragging = true;
            _dragBoundary = _hoverBoundary;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // --- FINISH SPLIT PLACEMENT ---
        if (_isPlacingSplit)
        {
            _isPlacingSplit = false;
            e.Pointer.Capture(null);
            e.Handled = true;

            if (Timeline != null && DataSource != null)
            {
                // 1. Hide the guide
                Timeline.GuideLinePosition = -1;

                // 2. Execute Split at the release position
                var point = e.GetPosition(this);
                double splitTime = Timeline.XToTime(point.X);
                double duration = Timeline.TotalDuration;

                var newInterval = DataSource.SplitInterval(splitTime, duration);
                
                // 3. Open Edit Dialog immediately
                if (newInterval != null)
                {
                    ShowEditDialog(newInterval);
                }
            }
            return;
        }

        if (_isSelectingIntervals)
        {
            _isSelectingIntervals = false;
            _selectionAnchorStart = -1;
            _selectionAnchorEnd = -1;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        // --- FINISH DRAG ---
        if (_isDragging)
        {
            if (_isSmartDragging)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    if (vm.HasOnnxModel)
                    {
                        // Existing async scoped realign — unchanged
                        int capturedLeft  = _smartDragLeftLimitIdx;
                        int capturedRight = _smartDragRightLimitIdx;

                        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                        {
                            var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
                            Frontend.Views.ProgressDialog? progressDialog = null;

                            try
                            {
                                if (topLevel != null)
                                {
                                    progressDialog = new Frontend.Views.ProgressDialog("Realigning...");
                                    progressDialog.Show(topLevel);
                                    await System.Threading.Tasks.Task.Delay(50);
                                }

                                await vm.RealignScopedAsync(capturedLeft, capturedRight);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Scoped Realign Error: {ex}");
                            }
                            finally
                            {
                                try { progressDialog?.Close(); } catch { }
                            }
                        });
                    }
                    else
                    {
                        // Elastic mode: one final synchronous redistribution, no dialog
                        if (_dragBoundary != null && DataSource != null
                            && _smartDragLeftLimitIdx >= 0 && _smartDragRightLimitIdx >= 0)
                        {
                            int pivotIdx = DataSource.Boundaries.IndexOf(_dragBoundary);
                            if (pivotIdx > 0)
                            {
                                Frontend.Services.Alignment.ElasticAligner.DistributeWithPivot(
                                    DataSource,
                                    _smartDragLeftLimitIdx,
                                    pivotIdx,
                                    _smartDragRightLimitIdx);
                            }
                        }
                    }
                }

                _isSmartDragging        = false;
                _smartDragLeftLimitIdx  = -1;
                _smartDragRightLimitIdx = -1;
            }

            _isDragging = false;
            _dragBoundary = null;
            e.Pointer.Capture(null);
            e.Handled = true;

            if (Timeline != null) Timeline.GuideLinePosition = -1;

            System.Diagnostics.Debug.WriteLine("Drag Finished.");
        }
    }
    #endregion

    #region Helpers
    internal static (double Top, double Bottom) CalculateMarkerVerticalBounds(double controlHeight)
    {
        double markerHalfThickness = HighlightPenThickness / 2.0;
        double baseInset = BorderThicknessPadding + MarkerSafetyPadding + markerHalfThickness;
        double shrink = AdditionalInteriorMargin;

        double topBound;
        double bottomBound;

        if (controlHeight <= (baseInset + shrink) * 2)
        {
            double center = controlHeight / 2.0;
            topBound = Math.Max(0, center - markerHalfThickness);
            bottomBound = Math.Min(controlHeight, center + markerHalfThickness);
        }
        else
        {
            topBound = Math.Max(markerHalfThickness, baseInset);
            bottomBound = Math.Min(controlHeight - markerHalfThickness, controlHeight - baseInset);
        }

        topBound += shrink;
        bottomBound -= shrink;

        if (bottomBound <= topBound)
        {
            double center = controlHeight / 2.0;
            topBound = Math.Max(0, center - markerHalfThickness);
            bottomBound = Math.Min(controlHeight, center + markerHalfThickness);
        }

        return (topBound, bottomBound);
    }
    #endregion
}
