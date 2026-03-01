namespace Frontend.Services.Alignment;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Frontend.Models;
using Frontend.Services.Audio;

public class SmartAligner : IAligner
{
    private readonly VadService _vadService;
    private readonly SimpleDurationEstimator _estimator;

    // Configuration for "Buffer Absorption"
    private const double MinGapForSilence = 0.300; // 300ms
    private const double BufferDuration = 0.100;   // 100ms padding into silence

    public SmartAligner()
    {
        _vadService = new VadService();
        _estimator = new SimpleDurationEstimator();
    }

    private struct CutPointInfo
    {
        public double MidTime;
        public double GapStart; // End of previous segment
        public double GapEnd;   // Start of next segment
        public bool IsVirtual;  // Start/End of file
        public double GapDuration => GapEnd - GapStart;
    }

    public async Task<TextGrid> AlignAsync(string text, string audioPath)
    {
        // 1. Run VAD (CPU intensive, run on thread)
        var segments = await Task.Run(() => _vadService.DetectSegments(audioPath));

        if (segments.Count == 0)
        {
            var grid = new TextGrid();
            grid.AddInterval(0, 10.0, text); // 10s default
            return grid;
        }

        double totalDuration = segments.Last().EndTime + 0.5; // Slightly beyond last speech
        
        // 2. Prepare Candidates (Cut Points) with Gap metadata
        var cutInfos = new List<CutPointInfo>();

        // 2a. Start of file (Virtual)
        cutInfos.Add(new CutPointInfo 
        { 
            MidTime = 0.0, 
            GapStart = 0.0, 
            GapEnd = segments[0].StartTime,
            IsVirtual = true
        });

        // 2b. Internal gaps
        for (int i = 0; i < segments.Count - 1; i++)
        {
            double endCurrent = segments[i].EndTime;
            double startNext = segments[i + 1].StartTime;
            double mid = (endCurrent + startNext) / 2.0;
            
            cutInfos.Add(new CutPointInfo
            {
                MidTime = mid,
                GapStart = endCurrent,
                GapEnd = startNext,
                IsVirtual = false
            });
        }

        // 2c. End of file (Virtual)
        cutInfos.Add(new CutPointInfo
        {
            MidTime = totalDuration,
            GapStart = segments.Last().EndTime,
            GapEnd = totalDuration,
            IsVirtual = true
        });

        // NEW: Calculate accumulated speech duration at each cut point
        var accumulatedSpeechTime = new double[cutInfos.Count];
        accumulatedSpeechTime[0] = 0;

        double runningTotal = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            runningTotal += segments[i].Duration;
            // cutInfos[i+1] corresponds to the gap AFTER segment[i]
            if (i + 1 < accumulatedSpeechTime.Length)
            {
                accumulatedSpeechTime[i + 1] = runningTotal;
            }
        }
        // cutInfos.Last is virtual end, same speech time as previous
        if (accumulatedSpeechTime.Length > segments.Count + 1)
        {
            accumulatedSpeechTime[segments.Count + 1] = runningTotal;
        }

        // 3. Prepare Text Phrases
        // Split by double newline or significant punctuation
        var rawLines = Regex.Split(text, @"(\r\n){2,}|\n{2,}")
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s.Trim())
                            .ToList();

        if (rawLines.Count == 0) return new TextGrid();

        // Calculate Weights
        var phraseWeights = new double[rawLines.Count];
        
        for (int i = 0; i < rawLines.Count; i++)
        {
            phraseWeights[i] = _estimator.EstimateWeight(rawLines[i]);
        }
        
        double totalWeight = phraseWeights.Sum();
        
        if (totalWeight <= 0) totalWeight = 1;

        // Assume total speaking time is roughly the duration of all speech segments
        double totalSpeechDuration = segments.Sum(s => s.Duration);
        double secondsPerWeight = totalSpeechDuration / totalWeight;

        // 4. Dynamic Programming
        int n = rawLines.Count;
        int m = cutInfos.Count;

        var dp = new double[n + 1, m];
        var parent = new int[n + 1, m];

        // Initialize with MaxValue
        for (int i = 0; i <= n; i++)
            for (int j = 0; j < m; j++)
                dp[i, j] = double.MaxValue;

        dp[0, 0] = 0; 

        for (int i = 1; i <= n; i++)
        {
            double expectedDur = phraseWeights[i - 1] * secondsPerWeight;

            // Heuristic: Don't check every single previous cutpoint. 
            // A phrase usually spans 1-10 segments.
            for (int j = i; j < m; j++) 
            {
                // Optimization: k only needs to go back somewhat reasonable distance
                // But given modern CPU, 50x50 is trivial.
                for (int k = i - 1; k < j; k++) 
                {
                    if (dp[i - 1, k] == double.MaxValue) continue;

                    // NEW 1: Smart Cost function (Compare Speech Time to Speech Time)
                    double actualSpeechDur = accumulatedSpeechTime[j] - accumulatedSpeechTime[k];
                    
                    double diff = actualSpeechDur - expectedDur;
                    double cost = (diff * diff); 

                    // NEW 2: Silence Barrier
                    bool bridgesHugeGap = false;
                    for (int p = k + 1; p <= j; p++) 
                    {
                        if (p < j && cutInfos[p].GapDuration > 2.0)
                        {
                            bridgesHugeGap = true;
                            break;
                        }
                    }

                    if (bridgesHugeGap) cost += 1000000; // Massive penalty

                    if (dp[i - 1, k] + cost < dp[i, j])
                    {
                        dp[i, j] = dp[i - 1, k] + cost;
                        parent[i, j] = k;
                    }
                }
            }
        }

        // 5. Backtracking
        // FIX: Force end at the last cutpoint (m-1) to ensure whole file is used.
        int bestEndIndex = m - 1;
        
        // If the cost is infinite (impossible), allow backing off
        if (dp[n, bestEndIndex] == double.MaxValue)
        {
            for (int j = m - 1; j >= n; j--)
            {
                if (dp[n, j] < double.MaxValue)
                {
                    bestEndIndex = j;
                    break;
                }
            }
        }

        var chosenIndices = new int[n + 1];
        chosenIndices[n] = bestEndIndex;
        for (int i = n; i > 0; i--)
        {
            chosenIndices[i - 1] = parent[i, chosenIndices[i]];
        }

        // 6. Build Grid with Buffer Absorption and Object Reuse
        var resultGrid = new TextGrid();
        
        // Track where the last interval effectively ended
        double lastWriteTime = 0.0;

        for (int i = 0; i < n; i++)
        {
            int idxStart = chosenIndices[i];
            int idxEnd = chosenIndices[i + 1];
            
            var startInfo = cutInfos[idxStart];
            var endInfo = cutInfos[idxEnd];

            double plannedStartTime, plannedEndTime;

            // --- Determine Start Time ---
            double gapBefore = startInfo.GapEnd - startInfo.GapStart;
            // Gap logic: if gap > 300ms, buffer 100ms. Else use midpoint.
            // MOD: Removed !IsVirtual check to allow initial silence
            if (gapBefore > MinGapForSilence)
            {
                plannedStartTime = startInfo.GapEnd - BufferDuration;
                if (plannedStartTime < startInfo.GapStart) plannedStartTime = startInfo.GapStart;
            }
            else
            {
                plannedStartTime = startInfo.MidTime;
            }

            // --- Determine End Time ---
            double gapAfter = endInfo.GapEnd - endInfo.GapStart;
            // MOD: Removed !IsVirtual check
            if (gapAfter > MinGapForSilence)
            {
                plannedEndTime = endInfo.GapStart + BufferDuration;
                if (plannedEndTime > endInfo.GapEnd) plannedEndTime = endInfo.GapEnd;
            }
            else
            {
                plannedEndTime = endInfo.MidTime;
            }
            
            // --- Helper to get or create start boundary linked to previous ---
            AlignmentBoundary GetNextStartBoundary(double time)
            {
                // Try to link to the previous interval's end to avoid duplicate markers
                if (resultGrid.Intervals.Count > 0)
                {
                    var probLast = resultGrid.Intervals.Last().End;
                    if (Math.Abs(probLast.Time - time) < 0.001)
                    {
                        return probLast;
                    }
                }
                
                // Otherwise create new (Start of file, or jump)
                var newBd = new AlignmentBoundary { Time = time, IsLocked = true };
                resultGrid.Boundaries.Add(newBd);
                return newBd;
            }

            // --- Fill Silence Gap from Previous ---
            // If the buffer logic moved our start time forward, leaving a hole from lastWriteTime
            if (plannedStartTime > lastWriteTime + 0.001)
            {
                var silStart = GetNextStartBoundary(lastWriteTime);
                var silEnd = new AlignmentBoundary { Time = plannedStartTime, IsLocked = true };
                resultGrid.Boundaries.Add(silEnd);

                var silenceInterval = new TextInterval
                {
                    Start = silStart,
                    End = silEnd,
                    Text = "_"
                };
                resultGrid.Intervals.Add(silenceInterval);
            }

            // --- Prepare Text ---
            // 1. Flatten internal newlines to " _ "
            // 2. Wrap in boundary silence tokens "_ ... _"
            string cleanContent = rawLines[i].Replace("\r", "").Replace("\n", " _ ").Trim();
            string finalContent = $"_ {cleanContent} _";

            // --- Add Text Interval ---
            var txtStart = GetNextStartBoundary(plannedStartTime);
            var txtEnd = new AlignmentBoundary { Time = plannedEndTime, IsLocked = true };
            resultGrid.Boundaries.Add(txtEnd);

            var textInterval = new TextInterval
            {
                Start = txtStart,
                End = txtEnd,
                Text = finalContent
            };
            resultGrid.Intervals.Add(textInterval);

            lastWriteTime = plannedEndTime;
        }

        // Fill remaining time at EOF with silence if needed
        double finalTime = cutInfos[m - 1].MidTime; // Should be totalDuration
        if (lastWriteTime < finalTime - 0.01)
        {
             // Determine start boundary (Reuse or New)
             AlignmentBoundary? endSilStart = null;
             if (resultGrid.Intervals.Count > 0)
             {
                 var lastEnd = resultGrid.Intervals.Last().End;
                 if (Math.Abs(lastEnd.Time - lastWriteTime) < 0.001) endSilStart = lastEnd;
             }
             if (endSilStart == null)
             {
                 endSilStart = new AlignmentBoundary { Time = lastWriteTime, IsLocked = true };
                 resultGrid.Boundaries.Add(endSilStart);
             }

             var endSilEnd = new AlignmentBoundary { Time = finalTime, IsLocked = true };
             resultGrid.Boundaries.Add(endSilEnd);

             var endSilence = new TextInterval
             {
                 Start = endSilStart,
                 End = endSilEnd,
                 Text = "_"
             };
             resultGrid.Intervals.Add(endSilence);
        }

        return resultGrid;
    }

    private TextGrid CreateBasicGrid(List<string> lines, double duration)
    {
        var grid = new TextGrid();
        double chunk = duration / lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            grid.AddInterval(i * chunk, (i + 1) * chunk, lines[i]);
        }
        return grid;
    }
}
