using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Frontend.Models;

public partial class TextGrid : ObservableObject
{
    [ObservableProperty]
    private string _name = "Phonemes";
    
    public ObservableCollection<TextInterval> Intervals { get; } = new();
    public ObservableCollection<AlignmentBoundary> Boundaries { get; } = new();

    /// <summary>
    /// Adds a new interval, creating or reusing boundaries as needed.
    /// Assumes intervals are added in chronological order.
    /// </summary>
    public void AddInterval(double start, double end, string text)
    {
        // 1. Get or Create Start Boundary
        var startB = Boundaries.FirstOrDefault(b => Math.Abs(b.Time - start) < 0.0001);
        if (startB == null)
        {
            startB = new AlignmentBoundary { Time = start, IsLocked = false };
            Boundaries.Add(startB);
        }

        // 2. Get or Create End Boundary
        var endB = Boundaries.FirstOrDefault(b => Math.Abs(b.Time - end) < 0.0001);
        if (endB == null)
        {
            endB = new AlignmentBoundary { Time = end, IsLocked = false };
            Boundaries.Add(endB);
        }

        // 3. Create Interval linked to these boundaries
        var interval = new TextInterval
        {
            Start = startB,
            End = endB,
            Text = text
        };
        Intervals.Add(interval);
    }

    /// <summary>
    /// Splits the interval at the given time. 
    /// If the grid is empty, it initializes it with boundaries at 0, time, and totalDuration.
    /// Returns the new interval created on the right side, or null if split failed.
    /// </summary>
    public TextInterval? SplitInterval(double time, double totalDuration)
    {
        // 1. Try to find existing interval to split
        var targetInterval = Intervals.FirstOrDefault(i => i.Start.Time < time && i.End.Time > time);
        
        if (targetInterval != null)
        {
            // --- EXISTING LOGIC for splitting ---
            var newBoundary = new AlignmentBoundary { Time = time, IsLocked = true };
            var newInterval = new TextInterval
            {
                Start = newBoundary,
                End = targetInterval.End, 
                Text = targetInterval.Text 
            };
            targetInterval.End = newBoundary;

            // Insert Boundary
            int boundaryInsertIndex = 0;
            while (boundaryInsertIndex < Boundaries.Count && Boundaries[boundaryInsertIndex].Time < time)
            {
                boundaryInsertIndex++;
            }
            Boundaries.Insert(boundaryInsertIndex, newBoundary);

            // Insert Interval
            int intervalIndex = Intervals.IndexOf(targetInterval);
            Intervals.Insert(intervalIndex + 1, newInterval);

            return newInterval;
        }
        
        // 2. Handle Empty Grid Case (Initialize Structure)
        if (Intervals.Count == 0)
        {
            // Ensure we have a Start Boundary at 0
            var startB = Boundaries.FirstOrDefault(b => Math.Abs(b.Time) < 0.001);
            if (startB == null)
            {
                startB = new AlignmentBoundary { Time = 0, IsLocked = true };
                if (Boundaries.Count > 0 && Boundaries[0].Time > 0) Boundaries.Insert(0, startB);
                else Boundaries.Add(startB);
            }

            // Ensure we have an End Boundary at TotalDuration
            var endB = Boundaries.FirstOrDefault(b => Math.Abs(b.Time - totalDuration) < 0.001);
            if (endB == null)
            {
                endB = new AlignmentBoundary { Time = totalDuration, IsLocked = true };
                Boundaries.Add(endB);
            }

            // Create the Split Boundary
            var splitB = new AlignmentBoundary { Time = time, IsLocked = true };
            
            // Insert split boundary in order
            Boundaries.Add(splitB);
            
            // Resort boundaries to ensure correct order {0, ..., time, ..., duration}
            var sorted = Boundaries.OrderBy(b => b.Time).ToList();
            Boundaries.Clear();
            foreach(var b in sorted) Boundaries.Add(b);

            // Re-fetch references from sorted list
            startB = Boundaries.First(); 
            endB = Boundaries.Last();
            splitB = Boundaries.First(b => Math.Abs(b.Time - time) < 0.001);

            // Create Intervals: [Start -> Split] and [Split -> End]
            // Left side (empty text)
            var leftInt = new TextInterval { Start = startB, End = splitB, Text = "_" };
            // Right side (empty text) - this is the "new" one we focus
            var rightInt = new TextInterval { Start = splitB, End = endB, Text = "_" };

            Intervals.Add(leftInt);
            Intervals.Add(rightInt);

            return rightInt;
        }

        return null;
    }

    /// <summary>
    /// Finds the nearest locked boundaries (or file start/end) around a given index.
    /// Returns indices of (LeftAnchor, RightAnchor).
    /// </summary>
    public (int LeftIndex, int RightIndex) FindSurroundingAnchors(int boundaryIndex)
    {
        if (Boundaries.Count == 0)
            return (0, 0);

        if (boundaryIndex < 0)
            boundaryIndex = 0;
        else if (boundaryIndex >= Boundaries.Count)
            boundaryIndex = Boundaries.Count - 1;

        int left = boundaryIndex - 1;
        while (left > 0 && !Boundaries[left].IsLocked)
        {
            left--;
        }
        if (left < 0)
            left = 0;

        int right = boundaryIndex + 1;
        while (right < Boundaries.Count - 1 && !Boundaries[right].IsLocked)
        {
            right++;
        }
        if (right >= Boundaries.Count)
            right = Boundaries.Count - 1;

        return (left, right);
    }

    /// <summary>
    /// Merges all intervals between the specified boundary indices into two large intervals
    /// surrounding the 'pivot' boundary.
    /// Used for Alt-Drag logic.
    /// </summary>
    public void MergeForSmartDrag(int leftAnchorIdx, int pivotIdx, int rightAnchorIdx)
    {
        if (Boundaries.Count < 3 || Intervals.Count < 2)
            return;

        if (leftAnchorIdx < 0 || pivotIdx <= leftAnchorIdx || rightAnchorIdx <= pivotIdx)
            return;

        if (rightAnchorIdx >= Boundaries.Count)
            rightAnchorIdx = Boundaries.Count - 1;

        if (leftAnchorIdx >= Intervals.Count || pivotIdx >= Intervals.Count)
            return;

        if (leftAnchorIdx + 2 >= Boundaries.Count || rightAnchorIdx - leftAnchorIdx < 2)
            return;

        var leftTarget = Intervals[leftAnchorIdx];
        string mergedLeftText = leftTarget.Text ?? string.Empty;

        for (int i = leftAnchorIdx + 1; i < pivotIdx && i < Intervals.Count; i++)
        {
            string txt = Intervals[i].Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(txt))
            {
                mergedLeftText = string.IsNullOrWhiteSpace(mergedLeftText)
                    ? txt
                    : $"{mergedLeftText} {txt}";
            }
        }
        leftTarget.Text = mergedLeftText;
        leftTarget.End = Boundaries[pivotIdx];

        string mergedRightText = string.Empty;
        for (int i = pivotIdx; i < rightAnchorIdx && i < Intervals.Count; i++)
        {
            string txt = Intervals[i].Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(txt))
            {
                mergedRightText = string.IsNullOrWhiteSpace(mergedRightText)
                    ? txt
                    : $"{mergedRightText} {txt}";
            }
        }

        for (int i = rightAnchorIdx - 1; i > pivotIdx; i--)
        {
            Boundaries.RemoveAt(i);
            if (i < Intervals.Count)
            {
                Intervals.RemoveAt(i);
            }
        }

        for (int i = pivotIdx - 1; i > leftAnchorIdx; i--)
        {
            Boundaries.RemoveAt(i);
            if (i < Intervals.Count)
            {
                Intervals.RemoveAt(i);
            }
        }

        var newRight = Intervals[leftAnchorIdx + 1];
        newRight.Start = Boundaries[leftAnchorIdx + 1]; // Pivot
        newRight.End = Boundaries[leftAnchorIdx + 2];   // Right anchor
        newRight.Text = mergedRightText;
    }

    /// <summary>
    /// Removes the specified boundary and merges the surrounding intervals.
    /// Returns true if successful, false if boundary is start/end and cannot be deleted.
    /// </summary>
    public bool DeleteBoundary(AlignmentBoundary boundary)
    {
        int index = Boundaries.IndexOf(boundary);

        // Constraint: Cannot delete the very first (Time 0) or very last boundary
        if (index <= 0 || index >= Boundaries.Count - 1) 
            return false;

        // Identify intervals to merge
        // Interval[i] starts at Boundary[i]. 
        // So Left Interval is [index-1], Right Interval is [index].
        var leftInterval = Intervals[index - 1];
        var rightInterval = Intervals[index];

        // 1. Merge Text
        string textA = leftInterval.Text ?? string.Empty;
        string textB = rightInterval.Text ?? string.Empty;

        string trimmedA = textA.Trim();
        string trimmedB = textB.Trim();

        if (string.IsNullOrWhiteSpace(trimmedA) && string.IsNullOrWhiteSpace(trimmedB))
        {
            leftInterval.Text = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(trimmedA))
        {
            leftInterval.Text = textB;
        }
        else if (string.IsNullOrWhiteSpace(trimmedB))
        {
            leftInterval.Text = textA;
        }
        else if (trimmedA == trimmedB)
        {
            // Same phoneme: keep only one
            leftInterval.Text = textA;
        }
        else
        {
            // Different phonemes: merge with space
            leftInterval.Text = textA + " " + textB;
        }

        // 2. Extend Left Interval Geometry
        leftInterval.End = rightInterval.End; 

        // 3. Remove Data
        Intervals.RemoveAt(index); // Remove the Right Interval
        Boundaries.RemoveAt(index); // Remove the Boundary itself

        return true;
    }

    /// <summary>
    /// Inserts a silence interval ("_") between selectionStart and selectionEnd.
    /// Handles Case 1 (one interior boundary) and Case 2 (no interior boundary).
    /// Returns an error string if the operation cannot proceed, or null on success.
    /// </summary>
    public string? InsertSilenceInterval(
        double selectionStart,
        double selectionEnd,
        string leftText,
        string rightText,
        bool isCase1)
    {
        if (isCase1)
        {
            // Case 1: exactly one interior boundary exists
            var marker = Boundaries.FirstOrDefault(b =>
                b.Time > selectionStart && b.Time < selectionEnd);
            if (marker == null) return "Interior boundary not found.";

            // Find the right interval whose Start == marker
            var rightInterval = Intervals.FirstOrDefault(i => i.Start == marker);
            if (rightInterval == null) return "Right interval not found.";

            // 1. Move marker to selectionStart
            marker.Time = selectionStart;
            marker.IsLocked = true;

            // 2. Create new boundary at selectionEnd
            var newMarker = new AlignmentBoundary { Time = selectionEnd, IsLocked = true };

            // 3. Insert newMarker into Boundaries list (in order)
            int insertPos = 0;
            while (insertPos < Boundaries.Count && Boundaries[insertPos].Time < selectionEnd)
                insertPos++;
            Boundaries.Insert(insertPos, newMarker);

            // 4. Update right interval to start at newMarker
            rightInterval.Start = newMarker;
            if (string.IsNullOrWhiteSpace(rightInterval.Text))
                rightInterval.Text = "_";

            // Normalize the left interval (the one whose End was moved)
            var leftInterval = Intervals.FirstOrDefault(i => i.End == marker);
            if (leftInterval != null && string.IsNullOrWhiteSpace(leftInterval.Text))
                leftInterval.Text = "_";

            // 5. Insert silence interval between marker and newMarker
            var silenceInterval = new TextInterval
            {
                Start = marker,
                End = newMarker,
                Text = "_"
            };

            int intervalInsertPos = Intervals.IndexOf(rightInterval);
            Intervals.Insert(intervalInsertPos, silenceInterval);

            return null; // success
        }
        else
        {
            // Case 2: no interior boundary — find the containing interval
            var containing = Intervals.FirstOrDefault(i =>
                i.Start.Time <= selectionStart && i.End.Time >= selectionEnd);
            if (containing == null) return "No interval contains the selection.";

            var originalEnd = containing.End;

            // 1. Create startMarker and endMarker
            var startMarker = new AlignmentBoundary { Time = selectionStart, IsLocked = true };
            var endMarker   = new AlignmentBoundary { Time = selectionEnd,   IsLocked = true };

            // 2. Insert both markers in order
            int startInsertPos = 0;
            while (startInsertPos < Boundaries.Count && Boundaries[startInsertPos].Time < selectionStart)
                startInsertPos++;
            Boundaries.Insert(startInsertPos, startMarker);

            int endInsertPos = 0;
            while (endInsertPos < Boundaries.Count && Boundaries[endInsertPos].Time < selectionEnd)
                endInsertPos++;
            Boundaries.Insert(endInsertPos, endMarker);

            // 3. Shorten the containing interval to end at startMarker
            containing.End = startMarker;
            containing.Text = string.IsNullOrWhiteSpace(leftText) ? "_" : leftText;

            // 4. Insert silence interval
            var silenceInterval = new TextInterval
            {
                Start = startMarker,
                End   = endMarker,
                Text  = "_" // Always "_"
            };

            int silenceInsertPos = Intervals.IndexOf(containing) + 1;
            Intervals.Insert(silenceInsertPos, silenceInterval);

            // 5. Insert right interval
            var rightInterval = new TextInterval
            {
                Start = endMarker,
                End   = originalEnd,
                Text  = string.IsNullOrWhiteSpace(rightText) ? "_" : rightText
            };
            Intervals.Insert(silenceInsertPos + 1, rightInterval);

            return null; // success
        }
    }
}
