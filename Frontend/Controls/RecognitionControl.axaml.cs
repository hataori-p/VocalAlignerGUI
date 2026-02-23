using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Frontend.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Frontend.Controls;

public partial class RecognitionControl : UserControl
{
    private const double FrameDuration = 0.02; // 20ms

    private readonly Typeface _font = new Typeface(FontFamily.Default);
    private readonly IPen _borderPen = new Pen(Brushes.WhiteSmoke, 0.5);

    private readonly IBrush _silBrush = Brushes.Transparent;
    private readonly IBrush _vowelBrush = new SolidColorBrush(Color.Parse("#FFB74D"));
    private readonly IBrush _consonantBrush = new SolidColorBrush(Color.Parse("#64B5F6"));
    private readonly IBrush _textBrush = Brushes.Black;

    public RecognitionControl()
    {
        InitializeComponent();
        Background = Brushes.Transparent;
        ClipToBounds = true;
    }

    public static readonly StyledProperty<TimelineState?> TimelineProperty =
        AvaloniaProperty.Register<RecognitionControl, TimelineState?>(nameof(Timeline));

    public TimelineState? Timeline
    {
        get => GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public static readonly StyledProperty<List<string>?> PhonemesProperty =
        AvaloniaProperty.Register<RecognitionControl, List<string>?>(nameof(Phonemes));

    public List<string>? Phonemes
    {
        get => GetValue(PhonemesProperty);
        set => SetValue(PhonemesProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TimelineProperty)
        {
            if (change.OldValue is TimelineState oldT) oldT.PropertyChanged -= OnStateChanged;
            if (change.NewValue is TimelineState newT) newT.PropertyChanged += OnStateChanged;
            InvalidateVisual();
        }

        if (change.Property == PhonemesProperty)
        {
            InvalidateVisual();
        }
    }

    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Timeline == null || Phonemes == null || Phonemes.Count == 0) return;

        double width = Bounds.Width;
        double height = Bounds.Height;
        double pixelsPerFrame = Timeline.PixelsPerSecond * FrameDuration;

        int startIdx = (int)(Timeline.VisibleStartTime / FrameDuration);
        int endIdx = (int)(Timeline.VisibleEndTime / FrameDuration) + 1;

        if (startIdx < 0) startIdx = 0;
        if (endIdx > Phonemes.Count) endIdx = Phonemes.Count;

        bool isZoomedIn = pixelsPerFrame > 20;

        if (isZoomedIn)
        {
            for (int i = startIdx; i < endIdx; i++)
            {
                string ph = Phonemes[i];
                double x = Timeline.TimeToX(i * FrameDuration);
                var rect = new Rect(x, 0, pixelsPerFrame, height);

                context.FillRectangle(GetBrushForPhoneme(ph), rect);
                context.DrawRectangle(_borderPen, rect);

                // Use the centralized IsSilence check to ensure '_' is also hidden
                if (!IsSilence(ph))
                {
                    var ft = new FormattedText(ph, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _font, 10, _textBrush);
                    if (ft.Width < pixelsPerFrame)
                    {
                        context.DrawText(ft, new Point(x + (pixelsPerFrame - ft.Width) / 2, (height - ft.Height) / 2));
                    }
                }
            }
        }
        else
        {
            double bucketSize = 2.0;
            int buckets = (int)(width / bucketSize) + 1;

            for (int b = 0; b < buckets; b++)
            {
                double x = b * bucketSize;
                double t1 = Timeline.XToTime(x);
                double t2 = Timeline.XToTime(x + bucketSize);

                int idx1 = (int)(t1 / FrameDuration);
                int idx2 = (int)(t2 / FrameDuration);

                if (idx1 >= Phonemes.Count) break;
                if (idx2 > Phonemes.Count) idx2 = Phonemes.Count;
                if (idx2 <= idx1) idx2 = idx1 + 1;

                bool hasConsonant = false;
                bool hasVowel = false;

                for (int k = idx1; k < idx2; k++)
                {
                    string p = Phonemes[k];
                    if (IsSilence(p)) continue;
                    if (IsVowel(p)) hasVowel = true;
                    else hasConsonant = true;
                }

                IBrush brush = _silBrush;
                if (hasConsonant) brush = _consonantBrush;
                else if (hasVowel) brush = _vowelBrush;

                if (brush != _silBrush)
                {
                    context.FillRectangle(brush, new Rect(x, 2, bucketSize, height - 4));
                }
            }
        }
    }

    // Treat additional markers like "_" as silence to keep the lane clean.
    private bool IsSilence(string ph) => ph == "sil" || ph == "<SIL>" || ph == "" || ph == "SP" || ph == " " || ph == "_";

    private bool IsVowel(string ph) => "aeiouɯyAEIOUYɴɑɐɒæɛɜəɪɨıɔɵøœɶɹʊʌʏ".Contains(ph.Substring(0, 1));

    private IBrush GetBrushForPhoneme(string ph)
    {
        if (IsSilence(ph)) return _silBrush;
        if (IsVowel(ph)) return _vowelBrush;
        return _consonantBrush;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
