using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Text.RegularExpressions;

namespace Frontend.Views;

public partial class IntervalEditDialog : Window
{
    public string ResultText { get; private set; } = string.Empty;
    public bool IsConfirmed { get; private set; } = false;

    private readonly bool _isComplexMode;
    // Increased estimate to account for Complex Mode height + Picker
    private const double MaxEstimatedHeight = 450;
    private const double PickerHeight = 220;

    public IntervalEditDialog()
    {
        InitializeComponent();
        this.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (InputBox is null)
        {
            throw new InvalidOperationException("InputBox control not found.");
        }

        if (IpaPicker is null)
        {
            throw new InvalidOperationException("IPA picker control not found.");
        }
        
        // Use AddHandler with RoutingStrategies.Tunnel to intercept keys before TextBox consumes them.
        // This fixes the issue where Ctrl+Enter inserts a newline instead of saving.
        InputBox.AddHandler(KeyDownEvent, OnInputBoxKeyDown, RoutingStrategies.Tunnel);
        
        IpaPicker.CharacterChosen += IpaPicker_CharacterChosen;
        InputBox.PastingFromClipboard += OnPastingFromClipboard;

        if (IpaToggle != null)
        {
            IpaToggle.IsCheckedChanged += OnIpaToggleChanged;
        }
    }

    public IntervalEditDialog(string initialText, string? hintOverride = null) : this()
    {
        initialText ??= string.Empty;

        string trimmed = initialText.Trim();
        bool isSilencePlaceholder = trimmed == "_" || trimmed == "sil" || trimmed == "sp";
        bool hasStructuralUnderscore = trimmed.Contains("_") && !isSilencePlaceholder;

        _isComplexMode = (trimmed.Length > 30) || trimmed.Contains("\n") || hasStructuralUnderscore;

        if (_isComplexMode)
        {
            InputBox.AcceptsReturn = true;
            InputBox.Height = 150;

            // CHANGE: Simply replace every underscore with underscore+newline to ensure consistent separation.
            string formatted = initialText.Replace("_", "_\n");
            InputBox.Text = formatted.Trim();

            var hint = this.FindControl<TextBlock>("HintText");
            if (hint != null)
            {
                hint.Text = "Ctrl+Enter to Save";
            }
        }
        else
        {
            InputBox.AcceptsReturn = false;
            InputBox.Height = double.NaN;
            InputBox.Text = initialText;

            var hint = this.FindControl<TextBlock>("HintText");
            if (hint != null)
            {
                hint.Text = "Enter to Save";
            }
        }

        if (hintOverride != null)
        {
            var hint = this.FindControl<TextBlock>("HintText");
            if (hint != null) hint.Text = hintOverride;
        }

        ResultText = initialText;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        EnsureOnScreen(); // Check position immediately on open

        var inputBox = InputBox;
        if (inputBox == null)
        {
            return;
        }

        inputBox.Focus();
        inputBox.SelectAll();
    }

    private void EnsureOnScreen(double extraHeight = 0)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var currentPos = Position;
        var screens = topLevel.Screens;
        if (screens == null) return;
        var screen = screens.ScreenFromPoint(currentPos) ?? screens.Primary;

        if (screen != null)
        {
            double currentHeight = Bounds.Height;
            if (currentHeight <= 0)
            {
                currentHeight = MaxEstimatedHeight;
            }

            // Calculate total projected height (Current + Expansion)
            double projectedBottom = currentPos.Y + currentHeight + extraHeight;
            double screenBottom = screen.WorkingArea.Bottom;

            if (projectedBottom > screenBottom)
            {
                double overlap = projectedBottom - screenBottom;
                // Move up by overlap + padding
                double newY = Math.Max(screen.WorkingArea.Y, currentPos.Y - overlap - 10);
                Position = new PixelPoint(currentPos.X, (int)newY);
            }
        }
    }

    private void OnInputBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (_isComplexMode)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    Commit();
                    e.Handled = true;
                }
            }
            else
            {
                Commit();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        Commit();
    }

    private void OnIpaToggleChanged(object? sender, RoutedEventArgs e)
    {
        bool isVisible = IpaToggle?.IsChecked ?? false;
        IpaPicker.IsVisible = isVisible;

        if (isVisible)
        {
            // Check if expanding the drawer pushes us off screen
            EnsureOnScreen(PickerHeight);
        }

        Dispatcher.UIThread.Post(() => InputBox.Focus());
    }

    private void IpaPicker_CharacterChosen(string character)
    {
        int caretIndex = InputBox.CaretIndex;
        string current = InputBox.Text ?? string.Empty;

        if (caretIndex < 0) caretIndex = 0;
        if (caretIndex > current.Length) caretIndex = current.Length;

        InputBox.Text = current.Insert(caretIndex, character);
        InputBox.CaretIndex = caretIndex + character.Length;
        InputBox.Focus();
    }

    private async void OnPastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null) return;

        string? text = await ClipboardExtensions.TryGetTextAsync(topLevel.Clipboard);
        if (string.IsNullOrEmpty(text)) return;

        text = NormalizeText(text);

        int idx = InputBox.CaretIndex;
        string current = InputBox.Text ?? string.Empty;

        if (idx < 0) idx = 0;
        if (idx > current.Length) idx = current.Length;

        InputBox.Text = current.Insert(idx, text);
        InputBox.CaretIndex = idx + text.Length;
    }

    private void Commit()
    {
        string raw = InputBox.Text ?? string.Empty;
        ResultText = NormalizeText(raw);
        IsConfirmed = true;
        Close();
    }

    private string NormalizeText(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string singleLine = input.Replace("\r\n", " ").Replace("\n", " ").Replace("\t", " ");
        string collapsed = Regex.Replace(singleLine, @"\s+", " ");

        return collapsed.Trim();
    }
}
