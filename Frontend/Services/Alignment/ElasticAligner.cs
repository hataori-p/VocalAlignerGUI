namespace Frontend.Services.Alignment;

using System;
using System.Collections.Generic;
using Frontend.Models;

/// <summary>
/// Pure, synchronous, allocation-light aligner used when no ONNX model is loaded.
/// Distributes boundary times proportionally based on phoneme weights.
///
/// Rules:
///   - Vowels / silence  → weight 1.0
///   - Consonants / unknown / empty → weight 0.3
///   - Locked boundaries are never moved; they act as hard anchors.
///   - The dragged boundary during elastic drag is treated as a temporary anchor.
/// </summary>
public static class ElasticAligner
{
    /// <summary>
    /// Redistributes all unlocked interior boundaries within [leftAnchorIdx, rightAnchorIdx].
    /// Called by the Realign button (no-model mode) for a single locked span.
    /// </summary>
    public static void DistributeInterval(
        TextGrid grid,
        int leftAnchorIdx,
        int rightAnchorIdx)
    {
        Redistribute(grid, leftAnchorIdx, rightAnchorIdx);
    }

    /// <summary>
    /// Redistributes boundaries within [leftAnchorIdx, pivotBoundaryIdx] and
    /// [pivotBoundaryIdx, rightAnchorIdx] independently.
    /// Called on every PointerMoved during elastic drag.
    /// </summary>
    public static void DistributeWithPivot(
        TextGrid grid,
        int leftAnchorIdx,
        int pivotBoundaryIdx,
        int rightAnchorIdx)
    {
        Redistribute(grid, leftAnchorIdx, pivotBoundaryIdx);
        Redistribute(grid, pivotBoundaryIdx, rightAnchorIdx);
    }

    /// <summary>
    /// Iterates the entire grid, finds every span between consecutive locked
    /// boundaries, and redistributes intervals within each span.
    /// Entry point for the Realign button in no-model mode.
    /// </summary>
    public static void DistributeEntireGrid(TextGrid grid)
    {
        if (grid.Boundaries.Count < 2)
            return;

        var anchorIndices = new List<int>();
        for (int i = 0; i < grid.Boundaries.Count; i++)
        {
            if (grid.Boundaries[i].IsLocked)
                anchorIndices.Add(i);
        }

        // Ensure file start and end are always anchors
        if (anchorIndices.Count == 0 || anchorIndices[0] != 0)
            anchorIndices.Insert(0, 0);
        if (anchorIndices[^1] != grid.Boundaries.Count - 1)
            anchorIndices.Add(grid.Boundaries.Count - 1);

        for (int s = 0; s < anchorIndices.Count - 1; s++)
        {
            int left  = anchorIndices[s];
            int right = anchorIndices[s + 1];
            if (right > left + 1)
                Redistribute(grid, left, right);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core
    // ─────────────────────────────────────────────────────────────────────────

    private static void Redistribute(TextGrid grid, int anchorLeft, int anchorRight)
    {
        if (anchorRight <= anchorLeft + 1)
            return;

        int intervalCount = anchorRight - anchorLeft;

        if (intervalCount <= 0)
            return;

        if (anchorLeft >= grid.Intervals.Count ||
            anchorLeft + intervalCount - 1 >= grid.Intervals.Count)
            return;

        // Collect per-interval weights
        var weights = new double[intervalCount];
        for (int i = 0; i < intervalCount; i++)
            weights[i] = PhonemeWeightCalculator.GetTotalWeight(grid.Intervals[anchorLeft + i].Text);

        double totalWeight = 0.0;
        foreach (var w in weights) totalWeight += w;
        if (totalWeight <= 0.0) totalWeight = intervalCount;

        double spanStart    = grid.Boundaries[anchorLeft].Time;
        double spanEnd      = grid.Boundaries[anchorRight].Time;
        double spanDuration = spanEnd - spanStart;

        double cursor = spanStart;
        for (int i = 0; i < intervalCount - 1; i++)
        {
            cursor += (weights[i] / totalWeight) * spanDuration;

            int boundaryIdx = anchorLeft + i + 1;

            // Never move a locked boundary that isn't one of our explicit anchors
            if (grid.Boundaries[boundaryIdx].IsLocked)
                continue;

            grid.Boundaries[boundaryIdx].Time = cursor;
        }
    }
}
