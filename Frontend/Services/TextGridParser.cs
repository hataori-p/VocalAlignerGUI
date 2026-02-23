using Frontend.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;

namespace Frontend.Services;

public static class TextGridParser
{
    /// <summary>
    /// Parses a TextGrid file and returns the most relevant tier.
    /// Priority:
    /// 1. Specific Tier Name (if provided)
    /// 2. Tier named "phons", "phonemes", or "phones" (Case Insensitive)
    /// 3. The first available IntervalTier
    /// </summary>
    public static async Task<TextGrid?> ParseAsync(string filePath, string? specificTierName = null)
    {
        if (!File.Exists(filePath)) return null;

        try 
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            var cleanLines = lines.Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            
            // Detect format
            bool isLong = cleanLines.Any(l => l.Contains("intervals [") || l.Contains("intervals: size"));
            
            // Parse ALL tiers
            var allTiers = isLong ? ParseLong(cleanLines) : ParseShort(cleanLines);
            
            if (allTiers.Count == 0) return null;

            // 1. Specific Request
            if (!string.IsNullOrWhiteSpace(specificTierName))
            {
                var match = allTiers.FirstOrDefault(t => t.Name.Equals(specificTierName, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
                // If specific tier not found, fallback or null? 
                // For now, let's return null to indicate not found.
                return null;
            }

            // 2. Priority Logic (Standard Workflow)
            var priority = allTiers.FirstOrDefault(t => 
                t.Name.Equals("phons", StringComparison.OrdinalIgnoreCase) || 
                t.Name.Equals("phonemes", StringComparison.OrdinalIgnoreCase) ||
                t.Name.Equals("phones", StringComparison.OrdinalIgnoreCase));

            if (priority != null) return priority;

            // 3. Fallback to First Tier
            return allTiers.FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Parser] Error: {ex}");
            return null;
        }
    }

    public static async Task SaveAsync(TextGrid grid, string filePath)
    {
        if (grid == null) throw new ArgumentNullException(nameof(grid));
        using var writer = new StreamWriter(filePath);

        string name = string.IsNullOrEmpty(grid.Name) ? "IntervalTier" : grid.Name;
        double xmin = grid.Intervals.Count > 0 ? grid.Intervals.First().Start.Time : 0;
        double xmax = grid.Intervals.Count > 0 ? grid.Intervals.Last().End.Time : 0;
        int size = grid.Intervals.Count;

        await writer.WriteLineAsync("File type = \"ooTextFile\"");
        await writer.WriteLineAsync("Object class = \"TextGrid\"");
        await writer.WriteLineAsync(string.Empty);
        await writer.WriteLineAsync(FormattableString.Invariant($"{xmin:F15}"));
        await writer.WriteLineAsync(FormattableString.Invariant($"{xmax:F15}"));
        await writer.WriteLineAsync("<exists>");
        await writer.WriteLineAsync("1");
        await writer.WriteLineAsync("\"IntervalTier\"");
        await writer.WriteLineAsync($"\"{name}\"");
        await writer.WriteLineAsync(FormattableString.Invariant($"{xmin:F15}"));
        await writer.WriteLineAsync(FormattableString.Invariant($"{xmax:F15}"));
        await writer.WriteLineAsync($"{size}");

        for (int i = 0; i < size; i++)
        {
            var interval = grid.Intervals[i];
            await writer.WriteLineAsync(FormattableString.Invariant($"{interval.Start.Time:F15}"));
            await writer.WriteLineAsync(FormattableString.Invariant($"{interval.End.Time:F15}"));
            string text = interval.Text ?? string.Empty;
            string escapedText = text.Replace("\"", "\"\"");
            await writer.WriteLineAsync($"\"{escapedText}\"");
        }
    }

    private static List<TextGrid> ParseLong(string[] lines)
    {
        var list = new List<TextGrid>();
        TextGrid? currentGrid = null;
        
        bool processingInterval = false;
        double xmin = -1, xmax = -1;
        string? text = null;

        foreach (var line in lines)
        {
            // Start of a new item (Tier)
            // Matches "item [1]:" etc.
            if (line.StartsWith("item [") && line.EndsWith("]:"))
            {
                currentGrid = new TextGrid();
                list.Add(currentGrid);
                processingInterval = false;
                continue;
            }

            if (currentGrid == null) continue;

            // Tier Metadata
            if (line.StartsWith("class =")) 
            {
                // We could filter out TextTiers here if we wanted to
            }
            else if (line.StartsWith("name =")) 
            {
                currentGrid.Name = ParseString(line);
            }

            // Interval Start
            else if (line.StartsWith("intervals [") || line.Contains("intervals: size")) 
            {
                processingInterval = true;
                xmin = -1; xmax = -1; text = null;
            }
            
            // Interval Data
            else if (processingInterval)
            {
                if (line.StartsWith("xmin =")) xmin = ParseVal(line);
                else if (line.StartsWith("xmax =")) xmax = ParseVal(line);
                else if (line.StartsWith("text ="))
                {
                    text = ParseString(line);
                    if (xmin >= 0 && xmax >= 0 && text != null)
                    {
                        currentGrid.AddInterval(xmin, xmax, text);
                        // Reset for safety, though next 'intervals [' handles it
                        xmin = -1; 
                    }
                }
            }
        }
        
        // Remove empty or non-IntervalTier grids (heuristic: count > 0 is good, but empty tiers exist)
        return list;
    }

    private static List<TextGrid> ParseShort(string[] lines)
    {
        var list = new List<TextGrid>();
        int i = 0;
        
        // Short format doesn't have explicit "item" blocks. It's sequential.
        // We scan for the class identifier "IntervalTier".
        
        while (i < lines.Length)
        {
            if (lines[i] == "\"IntervalTier\"")
            {
                var grid = new TextGrid();
                i++; // Skip class
                
                // Name
                if (i < lines.Length) grid.Name = ParseStringSimple(lines[i++]);
                
                // Time domain
                i++; // min
                i++; // max
                
                // Count
                if (i < lines.Length && int.TryParse(lines[i], out int count))
                {
                    i++;
                    for (int k = 0; k < count; k++)
                    {
                        if (i + 2 >= lines.Length) break;
                        double t1 = ParseValSimple(lines[i++]);
                        double t2 = ParseValSimple(lines[i++]);
                        string txt = ParseStringSimple(lines[i++]);
                        grid.AddInterval(t1, t2, txt);
                    }
                    list.Add(grid);
                }
            }
            else
            {
                // Skip lines that aren't starting a tier we know
                i++;
            }
        }

        return list;
    }

    private static double ParseVal(string line)
    {
        var parts = line.Split('=');
        if (parts.Length > 1 && double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v)) return v;
        return -1;
    }
    private static string ParseString(string line)
    {
        int s = line.IndexOf('"');
        int e = line.LastIndexOf('"');
        if (s != -1 && e > s) return line.Substring(s + 1, e - s - 1);
        return "";
    }
    private static double ParseValSimple(string line) => double.TryParse(line, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;
    private static string ParseStringSimple(string line) => line.Replace("\"", "");

    public static async Task<List<TextGrid>> ParseAllTiersAsync(string filePath)
    {
        if (!File.Exists(filePath)) return new List<TextGrid>();

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            var cleanLines = lines.Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

            bool isLong = cleanLines.Any(l => l.Contains("intervals [") || l.Contains("intervals: size"));

            return isLong ? ParseLong(cleanLines) : ParseShort(cleanLines);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Parser] Error: {ex}");
            return new List<TextGrid>();
        }
    }
}
