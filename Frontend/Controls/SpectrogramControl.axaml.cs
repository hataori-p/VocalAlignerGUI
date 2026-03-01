using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using static Avalonia.Input.Gestures;
using Avalonia.Media.Imaging;
using Frontend.Models;
using Frontend.Services;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Frontend.Controls;

public partial class SpectrogramControl : UserControl
{
    private Spectrogram.SpectrogramGenerator? _generator;
    
    // State
    private double _globalMaxDb = -60.0;     
    private double _globalMaxRmsDb = -60.0;  
    private bool _isDragging;
    private double _lastMouseX;
    private double _clickStartX;
    private double _selectionStartAnchor = -1;
    
    // Sync
    private double _actualStepSec;
    private double _fftOffsetSec;

    // Config
    private const double DynamicRangeDb = 70.0;
    private const int FftSize = 1024;
    private const double TargetTimeStepSec = 0.002; 
    private const int MaxTextureWidth = 8192; 
    private const double MaxAutoBoostDb = 40.0; 
    private const double PseudoDensityCorrectionDb = -20.0; 

    // Visuals
    private readonly IBrush _selectionBrush = new SolidColorBrush(Color.Parse("#33007ACC"));
    private readonly IPen _playheadPen = new Pen(Brushes.Red, 1);
    private readonly IPen _guidePen = new Pen(Brushes.Yellow, 1);

    #region Properties
    public static readonly StyledProperty<TimelineState?> TimelineProperty =
        AvaloniaProperty.Register<SpectrogramControl, TimelineState?>(nameof(Timeline));

    public TimelineState? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<AudioPlayerService?> PlayerServiceProperty =
        AvaloniaProperty.Register<SpectrogramControl, AudioPlayerService?>(nameof(PlayerService));

    public AudioPlayerService? PlayerService
    {
        get => GetValue(PlayerServiceProperty);
        set => SetValue(PlayerServiceProperty, value);
    }
    #endregion

    public SpectrogramControl()
    {
        InitializeComponent();
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TimelineProperty)
        {
            if (change.OldValue is TimelineState oldT) oldT.PropertyChanged -= OnTimelineChanged;
            if (change.NewValue is TimelineState newT)
            {
                newT.PropertyChanged += OnTimelineChanged;
                if (Bounds.Width > 0) newT.CanvasWidth = Bounds.Width;
                InitializeGenerator(); 
                InvalidateVisual();
            }
        }
        else if (change.Property == PlayerServiceProperty)
        {
            InitializeGenerator();
            InvalidateVisual();
        }
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs e)
    {
        InvalidateVisual();
        if (e.PropertyName == nameof(TimelineState.CurrentFileHash))
        {
            InitializeGenerator();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (Timeline != null) Timeline.CanvasWidth = e.NewSize.Width;
        InvalidateVisual();
    }

    private void InitializeGenerator()
    {
        _generator = null;
        if (PlayerService?.SpectrogramData == null || PlayerService.SampleRate <= 0) return;

        try
        {
            int stepSizeSamples = (int)(PlayerService.SampleRate * TargetTimeStepSec);
            _actualStepSec = (double)stepSizeSamples / PlayerService.SampleRate;
            _fftOffsetSec = (double)FftSize / 2.0 / PlayerService.SampleRate;

            _generator = new Spectrogram.SpectrogramGenerator(PlayerService.SampleRate, FftSize, stepSizeSamples);
            _generator.Add(PlayerService.SpectrogramData);

            CalculateGlobalStats();
        }
        catch { /* Ignore init errors */ }
    }

    private void CalculateGlobalStats()
    {
        if (_generator == null) return;
        
        // 1. FFT Stats
        var ffts = _generator.GetFFTs();
        if (ffts.Count > 0)
        {
            double maxDb = -200.0;
            int width = ffts.Count;
            int height = ffts[0].Length; 
            
            for (int x = 0; x < width; x += 10)
            {
                for (int y = 1; y < height; y++) 
                {
                    double m = ffts[x][y];
                    double db = (m <= 1e-9) ? -200.0 : 20.0 * Math.Log10(m);
                    if (db > maxDb) maxDb = db;
                }
            }
            _globalMaxDb = Math.Max(maxDb, -60.0);
        }

        // 2. RMS Stats
        double maxRmsDb = -200.0;
        var data = PlayerService!.SpectrogramData!;
        int window = PlayerService.SampleRate / 100;
        
        for(int i = 0; i < data.Length; i += window)
        {
            double sumSq = 0;
            int count = 0;
            for(int j=0; j<window && (i+j) < data.Length; j++) { sumSq += data[i+j] * data[i+j]; count++; }
            
            if (count > 0)
            {
                double rms = Math.Sqrt(sumSq / count);
                double db = (rms <= 1e-9) ? -200.0 : 20.0 * Math.Log10(rms);
                if (db > maxRmsDb) maxRmsDb = db;
            }
        }
        _globalMaxRmsDb = Math.Max(maxRmsDb, -60.0);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.White, new Rect(Bounds.Size));

        if (_generator == null || Timeline == null) return;

        double dur = Timeline.VisibleEndTime - Timeline.VisibleStartTime;
        double columns = dur / _actualStepSec;

        // Adaptive Rendering: Use Pseudo (Bars) if zoomed out too far, otherwise Real (FFT Bitmap)
        if (columns > (MaxTextureWidth - 50)) RenderPseudo(context);
        else RenderReal(context);

        DrawOverlays(context);
    }

    private void RenderReal(DrawingContext context)
    {
        double fftPerSec = 1.0 / _actualStepSec; 
        double startT = Timeline!.VisibleStartTime - _fftOffsetSec;
        double endT = Timeline.VisibleEndTime - _fftOffsetSec;
        
        int startIdx = Math.Max(0, (int)(startT * fftPerSec));
        int endIdx = Math.Min(_generator!.GetFFTs().Count, (int)(Math.Ceiling(endT * fftPerSec)));
        
        if (startIdx >= endIdx) return;

        // Auto-Contrast calculation (Local vs Global)
        double localMax = _isDragging ? _globalMaxDb : ScanLocalMax(startIdx, endIdx);
        double effectiveMax = Math.Max(localMax, _globalMaxDb - MaxAutoBoostDb);

        var bitmap = GenerateBitmap(startIdx, endIdx, effectiveMax);
        if (bitmap == null) return;

        double sliceStart = (startIdx * _actualStepSec) + _fftOffsetSec;
        double sliceEnd = (endIdx * _actualStepSec) + _fftOffsetSec;
        
        var rect = new Rect(Timeline.TimeToX(sliceStart), 0, Timeline.TimeToX(sliceEnd) - Timeline.TimeToX(sliceStart), Bounds.Height);
        context.DrawImage(bitmap, rect);
    }

    private double ScanLocalMax(int start, int end)
    {
        double max = -200.0;
        var ffts = _generator!.GetFFTs();
        int step = Math.Max(1, (end - start) / 200);

        for (int i = start; i < end; i += step)
        {
            var fft = ffts[i];
            for (int j = 5; j < fft.Length; j += 5) 
            {
                if (fft[j] > 0)
                {
                    double db = 20.0 * Math.Log10(fft[j]);
                    if (db > max) max = db;
                }
            }
        }
        return max;
    }

    private WriteableBitmap? GenerateBitmap(int start, int end, double maxDb)
    {
        int w = end - start;
        int h = _generator!.GetFFTs()[0].Length;
        if (w <= 0 || w > MaxTextureWidth) return null;

        var pixels = new uint[w * h];
        double minDb = maxDb - DynamicRangeDb;
        var ffts = _generator.GetFFTs();

        for (int x = 0; x < w; x++)
        {
            double[] fft = ffts[start + x];
            for (int y = 0; y < h; y++)
            {
                // Invert Y (Low freq at bottom)
                int py = (h - 1) - y;
                double m = fft[y];
                double db = (m <= 1e-9) ? -200.0 : 20.0 * Math.Log10(m);
                
                double r = (db - minDb) / DynamicRangeDb;
                r = Math.Clamp(r, 0, 1);
                
                byte v = (byte)(255 * (1.0 - r)); // Inverted grayscale
                pixels[py * w + x] = (uint)((255 << 24) | (v << 16) | (v << 8) | v);
            }
        }

        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using var buf = bmp.Lock();
        Marshal.Copy((int[])(object)pixels, 0, buf.Address, pixels.Length);
        return bmp;
    }

    private void RenderPseudo(DrawingContext context)
    {
        // Simple RMS-based rendering for high zoom-out
        var data = PlayerService!.SpectrogramData!;
        int total = data.Length;
        int startSample = Math.Clamp((int)(Timeline!.VisibleStartTime * PlayerService.SampleRate), 0, total);
        int endSample = Math.Clamp((int)(Timeline.VisibleEndTime * PlayerService.SampleRate), 0, total);
        
        int samples = endSample - startSample;
        if (samples <= 0 || Bounds.Width <= 0) return;

        double spp = samples / Bounds.Width;
        double minDb = Math.Max(_globalMaxRmsDb, _globalMaxRmsDb - MaxAutoBoostDb) - DynamicRangeDb;

        for (int x = 0; x < Bounds.Width; x++)
        {
            int cs = startSample + (int)(x * spp);
            int ce = startSample + (int)((x + 1) * spp);
            if (cs >= total) break;
            
            double sumSq = 0;
            int c = 0;
            // Optimize stride
            int stride = Math.Max(1, (ce-cs)/10); 
            for(int i=cs; i<Math.Min(ce, total); i+=stride) { sumSq += data[i]*data[i]; c++; }

            if (c > 0)
            {
                double db = (Math.Sqrt(sumSq/c) <= 1e-9) ? -200 : 20.0 * Math.Log10(Math.Sqrt(sumSq/c));
                db += PseudoDensityCorrectionDb;
                double r = Math.Clamp((db - minDb) / DynamicRangeDb, 0, 1);
                byte v = (byte)(255 * (1.0 - r));
                context.FillRectangle(new SolidColorBrush(Color.FromRgb(v,v,v)), new Rect(x, 0, 1.5, Bounds.Height));
            }
        }
    }

    private void DrawOverlays(DrawingContext context)
    {
        if (Timeline!.IsSelectionActive)
        {
            double x1 = Timeline.TimeToX(Timeline.SelectionStartTime);
            double w = Timeline.TimeToX(Timeline.SelectionEndTime) - x1;
            context.FillRectangle(_selectionBrush, new Rect(x1, 0, w, Bounds.Height));
        }
    }

    // --- Interaction ---
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (Timeline == null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Timeline.ScrollByRatio(-e.Delta.Y * 0.1);
        }
        else
        {
            // Zoom centered on mouse
            double dur = Timeline.VisibleEndTime - Timeline.VisibleStartTime;
            double factor = e.Delta.Y > 0 ? 1/1.2 : 1.2;
            double newDur = dur * factor;
            
            double mouseRatio = e.GetPosition(this).X / Bounds.Width;
            double timeAtMouse = Timeline.VisibleStartTime + (dur * mouseRatio);
            
            Timeline.SetView(timeAtMouse - (newDur * mouseRatio), timeAtMouse + (newDur * (1-mouseRatio)));
        }
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Timeline == null) return;
        var pt = e.GetPosition(this);
        double t = Timeline.XToTime(pt.X);

        var vm = DataContext as Frontend.ViewModels.MainWindowViewModel;
        bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || (vm?.IsVirtualShiftActive == true);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastMouseX = pt.X;
            _clickStartX = pt.X; // Start drag detection
            e.Pointer.Capture(this);

            if (isShift)
            {
                _selectionStartAnchor = t;
                Timeline.SetSelection(t, t);
            }
            else
            {
                _selectionStartAnchor = -1;
            }
            e.Handled = true;
        }
        else if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            if (Timeline == null) return;
            double timeClicked = Timeline.XToTime(pt.X);

            if (vm != null)
            {
                double endTime = isShift
                    ? -1
                    : Timeline.VisibleEndTime;

                vm.PlayRange(timeClicked, endTime);
            }
            e.Handled = true;
            vm?.ConsumeOneShotModifiers();
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || Timeline == null) return;
        var pt = e.GetPosition(this);
        double t = Timeline.XToTime(pt.X);

        var vm = DataContext as Frontend.ViewModels.MainWindowViewModel;
        bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || (vm?.IsVirtualShiftActive == true);

        if (_selectionStartAnchor >= 0 && isShift)
        {
            Timeline.SetSelection(_selectionStartAnchor, t);
        }
        else
        {
            // Pan logic
            if (!Timeline.IsInteracting && Math.Abs(pt.X - _clickStartX) > 3) Timeline.IsInteracting = true;
            
            if (Timeline.IsInteracting)
            {
                double dt = (pt.X - _lastMouseX) / Timeline.PixelsPerSecond;
                Timeline.SetView(Timeline.VisibleStartTime - dt, Timeline.VisibleEndTime - dt);
                _lastMouseX = pt.X;
            }
        }
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            Timeline!.IsInteracting = false;
            e.Pointer.Capture(null);

            var vm = DataContext as Frontend.ViewModels.MainWindowViewModel;
            bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || (vm?.IsVirtualShiftActive == true);

            // Handle Click (Seek)
            if (Math.Abs(e.GetPosition(this).X - _clickStartX) < 3 && _selectionStartAnchor < 0)
            {
                Timeline.ClearSelection();
                // Safe invoke seek command via ViewModel binding or direct property
                if (vm != null) 
                    vm.SeekToCommand.Execute(Timeline.XToTime(e.GetPosition(this).X));
            }
            
            vm?.ConsumeOneShotModifiers();

            InvalidateVisual(); // Force high-quality render
            e.Handled = true;
        }
    }
}
