using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Frontend.ViewModels;

namespace Frontend.Controls;

public partial class ControlsHelpPanel : UserControl
{
    public ControlsHelpPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && MarkdownText != null)
        {
            RenderMarkdown(vm.ControlsMarkdown);
        }
    }

    private void RenderMarkdown(string markdown)
    {
        if (MarkdownText == null) return;

        MarkdownText.Inlines?.Clear();
        var inlines = MarkdownText.Inlines ??= new InlineCollection();

        var lines = markdown.Split('\n');
        var tableBuffer = new List<string>();
        bool inTable = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r');

            // Detect table start/continuation
            if (trimmed.StartsWith("|"))
            {
                tableBuffer.Add(trimmed);
                inTable = true;
                continue;
            }
            
            // End of table - render it
            if (inTable)
            {
                RenderTable(inlines, tableBuffer);
                tableBuffer.Clear();
                inTable = false;
            }

            // Heading 1
            if (trimmed.StartsWith("# "))
            {
                inlines.Add(new Run(trimmed[2..] + "\n") 
                { 
                    FontSize = 24, 
                    FontWeight = FontWeight.Bold 
                });
                continue;
            }

            // Heading 2
            if (trimmed.StartsWith("## "))
            {
                inlines.Add(new Run("\n" + trimmed[3..] + "\n") 
                { 
                    FontSize = 18, 
                    FontWeight = FontWeight.Bold 
                });
                continue;
            }

            // Heading 3
            if (trimmed.StartsWith("### "))
            {
                inlines.Add(new Run("\n" + trimmed[4..] + "\n") 
                { 
                    FontSize = 15, 
                    FontWeight = FontWeight.Bold 
                });
                continue;
            }

            // Horizontal rule
            if (trimmed.StartsWith("---"))
            {
                inlines.Add(new Run("────────────────────────────────────────\n") 
                { 
                    Foreground = Brushes.Gray 
                });
                continue;
            }

            // List item
            if (trimmed.StartsWith("* ") || trimmed.StartsWith("- "))
            {
                ParseInlineFormatting(inlines, "  • " + trimmed[2..], false);
                inlines.Add(new Run("\n"));
                continue;
            }

            // Empty line
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                inlines.Add(new Run("\n"));
                continue;
            }

            // Regular text with inline formatting
            ParseInlineFormatting(inlines, trimmed, false);
            inlines.Add(new Run("\n"));
        }

        // Handle table at end of file
        if (inTable && tableBuffer.Count > 0)
        {
            RenderTable(inlines, tableBuffer);
        }
    }

    private void RenderTable(InlineCollection inlines, List<string> tableLines)
    {
        if (tableLines.Count == 0) return;

        // Parse all rows, skip separator rows
        var dataRows = new List<string[]>();
        foreach (var line in tableLines)
        {
            // Skip separator rows like |:---|:---|
            if (line.Contains(":---") || line.Contains("---:") || 
                line.Replace("|", "").Replace("-", "").Replace(":", "").Trim() == "")
            {
                continue;
            }

            var cells = line.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .ToArray();
            if (cells.Length > 0)
                dataRows.Add(cells);
        }

        if (dataRows.Count == 0) return;

        // Calculate column widths
        int colCount = dataRows.Max(r => r.Length);
        var colWidths = new int[colCount];
        
        foreach (var row in dataRows)
        {
            for (int c = 0; c < row.Length; c++)
            {
                // Strip formatting for width calculation
                string plain = StripFormatting(row[c]);
                colWidths[c] = Math.Max(colWidths[c], plain.Length);
            }
        }

        // Render header row
        bool isHeader = true;
        foreach (var row in dataRows)
        {
            for (int c = 0; c < colCount; c++)
            {
                string cellText = c < row.Length ? row[c] : "";
                string plainText = StripFormatting(cellText);
                int padding = colWidths[c] - plainText.Length;

                if (isHeader)
                {
                    // Header cells in bold
                    inlines.Add(new Run(plainText + new string(' ', padding + 2)) 
                    { 
                        FontWeight = FontWeight.Bold,
                        FontFamily = new FontFamily("Consolas, Courier New, monospace")
                    });
                }
                else
                {
                    // Data cells with formatting
                    ParseInlineFormatting(inlines, cellText, true);
                    inlines.Add(new Run(new string(' ', padding + 2)) 
                    { 
                        FontFamily = new FontFamily("Consolas, Courier New, monospace")
                    });
                }
            }
            inlines.Add(new Run("\n"));
            
            // Add separator after header
            if (isHeader)
            {
                // Use shorter dashes (cap at 20 chars per column) to prevent line wrapping
                string separator = string.Join("  ", colWidths.Select(w => new string('─', Math.Min(w, 20))));
                inlines.Add(new Run(separator + "\n") 
                { 
                    Foreground = Brushes.Gray,
                    FontFamily = new FontFamily("Consolas, Courier New, monospace")
                });
                isHeader = false;
            }
        }
        inlines.Add(new Run("\n"));
    }

    private string StripFormatting(string text)
    {
        // Remove markdown formatting
        text = Regex.Replace(text, @"\*\*\*(.+?)\*\*\*", "$1");
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        text = Regex.Replace(text, @"\*(.+?)\*", "$1");
        text = Regex.Replace(text, @"`(.+?)`", "$1");
        // Remove HTML tags
        text = Regex.Replace(text, @"<[^>]+>", "");
        // Remove HTML entities
        text = text.Replace("&nbsp;", " ");
        return text;
    }


    private void ParseInlineFormatting(InlineCollection inlines, string text, bool mono)
    {
        var monoFont = new FontFamily("Consolas, Courier New, monospace");
        
        // Handle HTML color spans
        var colorPattern = @"<span\s+style\s*=\s*""color:\s*(\w+)""\s*>(.+?)</span>";
        
        if (Regex.IsMatch(text, colorPattern, RegexOptions.IgnoreCase))
        {
            int lastIdx = 0;
            foreach (Match match in Regex.Matches(text, colorPattern, RegexOptions.IgnoreCase))
            {
                if (match.Index > lastIdx)
                {
                    ParseMarkdownFormatting(inlines, text[lastIdx..match.Index], mono, monoFont);
                }

                string colorName = match.Groups[1].Value.ToLower();
                string content = match.Groups[2].Value;
                
                var brush = colorName switch
                {
                    "blue" => Brushes.DodgerBlue,
                    "red" => Brushes.Tomato,
                    "yellow" => Brushes.Yellow,
                    "green" => Brushes.LimeGreen,
                    _ => Brushes.White
                };

                string plainContent = StripFormatting(content);
                var run = new Run(plainContent) 
                { 
                    Foreground = brush,
                    FontWeight = FontWeight.Bold
                };
                if (mono) run.FontFamily = monoFont;
                inlines.Add(run);

                lastIdx = match.Index + match.Length;
            }

            if (lastIdx < text.Length)
            {
                ParseMarkdownFormatting(inlines, text[lastIdx..], mono, monoFont);
            }
        }
        else
        {
            text = text.Replace("<br>", " ").Replace("<br/>", " ").Replace("<br />", " ");
            ParseMarkdownFormatting(inlines, text, mono, monoFont);
        }
    }

    private void ParseMarkdownFormatting(InlineCollection inlines, string text, bool mono, FontFamily monoFont)
    {
        var pattern = @"(\*\*\*(.+?)\*\*\*|\*\*(.+?)\*\*|\*(.+?)\*|`(.+?)`)";
        var regex = new Regex(pattern);

        int lastIndex = 0;
        foreach (Match match in regex.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                var run = new Run(text[lastIndex..match.Index]);
                if (mono) run.FontFamily = monoFont;
                inlines.Add(run);
            }

            if (match.Groups[2].Success)
            {
                var run = new Run(match.Groups[2].Value) 
                { 
                    FontWeight = FontWeight.Bold, 
                    FontStyle = FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(130, 90, 0))
                };
                if (mono) run.FontFamily = monoFont;
                inlines.Add(run);
            }
            else if (match.Groups[3].Success)
            {
                var run = new Run(match.Groups[3].Value) 
                { 
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 50, 150))
                };
                if (mono) run.FontFamily = monoFont;
                inlines.Add(run);
            }
            else if (match.Groups[4].Success)
            {
                var run = new Run(match.Groups[4].Value) 
                { 
                    FontStyle = FontStyle.Italic,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 80, 100))
                };
                if (mono) run.FontFamily = monoFont;
                inlines.Add(run);
            }
            else if (match.Groups[5].Success)
            {
                inlines.Add(new Run(match.Groups[5].Value) 
                { 
                    FontFamily = monoFont,
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 100)),
                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 60))
                });
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            var run = new Run(text[lastIndex..]);
            if (mono) run.FontFamily = monoFont;
            inlines.Add(run);
        }
    }
}
