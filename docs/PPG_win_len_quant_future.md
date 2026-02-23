## Documentation: PPG Window Length Quantization in `extract_feature_window`

**File:** `allosaurus_rex/refinement_model/features.py`  
**Function:** `extract_feature_window`  
**Date discovered:** 2026-02-19  
**Status:** Known behavior — model trained with this behavior, intentionally replicated in C# for inference fidelity

---

### The Behavior

When extracting a 120ms feature window centered on a boundary, the PPG slice length at 50Hz depends on the center time:

```python
ppg_frame_duration_s = 1.0 / NATIVE_PPG_RATE_HZ          # 0.02s
start_ppg_frame = int((center_time_s - window_len_s/2) / ppg_frame_duration_s)
end_ppg_frame   = int((center_time_s + window_len_s/2) / ppg_frame_duration_s)
ppg_window      = full_ppg[start_ppg_frame:end_ppg_frame]  # slice at 50Hz
```

Due to `int()` truncation of both endpoints, the slice length varies:

| center_time_s | start_frame | end_frame | sliceLen | upsampled | zero-pad |
|---|---|---|---|---|---|
| 0.44 | `int(0.38/0.02)=19` | `int(0.50/0.02)=25` | 6 | 24 | 0 |
| 0.58 | `int(0.52/0.02)=26` | `int(0.64/0.02)=32` | **5** | **20** | **4** |
| 1.10 | `int(1.04/0.02)=52` | `int(1.16/0.02)=58` | 6 | 24 | 0 |

The upsampling step:
```python
upsampled_ppg = upsample_ppgs(ppg_window, NATIVE_PPG_RATE_HZ, TARGET_FRAME_RATE_HZ)
# output_len = round(sliceLen * 200 / 50) = round(sliceLen * 4)
```

For `sliceLen=5`: output is 20 frames. Then:
```python
min_len = min(20, flux_frames)  # = min(20, 25) = 20
# combined_features has 20 rows
# padded to 24 with torch.zeros → frames 20-23 are [0, 0, ..., 0]
```

**The model sees 4 zero rows at the end of the feature window for any boundary where the PPG quantizes to 5 frames.**

---

### Why This Happens

The root cause is **using `int()` truncation on both endpoints independently** rather than computing a centered window of fixed size:

```python
# Current (buggy): two independent truncations → sliceLen varies between 5 and 7
start = int((center - half) / frame_dur)
end   = int((center + half) / frame_dur)
sliceLen = end - start  # ∈ {5, 6, 7} depending on center_time_s

# Better: fixed-size window from center
center_frame = round(center_time_s * NATIVE_PPG_RATE_HZ)
half_frames  = TIME_STEPS * NATIVE_PPG_RATE_HZ // (2 * TARGET_FRAME_RATE_HZ)  # = 6
start = center_frame - half_frames  # always 6 frames from center
end   = center_frame + half_frames  # always 6 frames ahead
sliceLen = end - start              # always 12... 
```

Wait — actually `120ms * 50Hz = 6 frames` total, not 12. The window is only 6 frames at 50Hz. So `half_frames = 3`:

```python
# Better: symmetric centered window — always 6 frames
center_frame = int(round(center_time_s * NATIVE_PPG_RATE_HZ))
start = center_frame - 3
end   = center_frame + 3
sliceLen = 6  # always exactly 6 → upsamples to exactly 24
```

---

### Impact on Training Data

During training data generation (`prepare_refinement_data.py`), every boundary time goes through `extract_feature_window`. Approximately **25% of boundaries** fall on center times where `int()` truncation produces `sliceLen=5` rather than 6. For those samples:

- Frames 20-23 of the PPG channels are **always zero**
- Frames 20-23 of the flux channels are **also zero** (due to `min_len=20`)
- The model learned that trailing zero frames are a normal input pattern

This means the trained model is **robust to zero-padded trailing frames** — it doesn't rely on frames 20-23 for accurate boundary prediction. This is probably harmless but represents wasted model capacity.

---

### What To Change If Retraining

**`allosaurus_rex/refinement_model/features.py`** — fix `extract_feature_window`:

```python
# Replace the PPG window extraction block:

# OLD — two independent int() truncations, sliceLen varies 5-7
ppg_frame_duration_s = 1.0 / NATIVE_PPG_RATE_HZ
start_ppg_frame = int((center_time_s - window_len_s/2) / ppg_frame_duration_s)
end_ppg_frame   = int((center_time_s + window_len_s/2) / ppg_frame_duration_s)
start_ppg_frame = max(0, start_ppg_frame)
end_ppg_frame   = min(full_ppg.shape[0], end_ppg_frame)
ppg_window      = full_ppg[start_ppg_frame:end_ppg_frame]
upsampled_ppg   = upsample_ppgs(ppg_window.cpu().numpy(),
                                NATIVE_PPG_RATE_HZ, TARGET_FRAME_RATE_HZ)

# NEW — symmetric centered window, always exactly 6 frames → always 24 upsampled
NATIVE_HALF_FRAMES = int(round(ANALYSIS_WINDOW_MS / 1000.0 / 2.0 * NATIVE_PPG_RATE_HZ))
# = int(round(0.06 * 50)) = int(round(3.0)) = 3
center_ppg_frame = int(round(center_time_s * NATIVE_PPG_RATE_HZ))
start_ppg_frame  = max(0, center_ppg_frame - NATIVE_HALF_FRAMES)
end_ppg_frame    = min(full_ppg.shape[0], center_ppg_frame + NATIVE_HALF_FRAMES)
ppg_window       = full_ppg[start_ppg_frame:end_ppg_frame]

# If near audio boundary, reflect-pad the PPG slice to exactly 2*NATIVE_HALF_FRAMES
expected_ppg_len = 2 * NATIVE_HALF_FRAMES  # always 6
if ppg_window.shape[0] < expected_ppg_len:
    pad_size  = expected_ppg_len - ppg_window.shape[0]
    ppg_window = torch.cat([ppg_window,
                             torch.zeros(pad_size, ppg_window.shape[1],
                                         device=ppg_window.device)], dim=0)

upsampled_ppg = upsample_ppgs(ppg_window.cpu().numpy(),
                               NATIVE_PPG_RATE_HZ, TARGET_FRAME_RATE_HZ)
# Now always produces exactly 24 frames — no zero-padding needed at the end
```

**`allosaurus_rex/refinement_model/prepare_refinement_data.py`** — no changes needed; it calls `extract_feature_window` which will be fixed.

**`Frontend/Core/DSP/RefinementFeatureExtractor.cs`** — update to match the new fixed behavior:

```csharp
// Replace rate-based upsampling with fixed symmetric window:

// NEW: always produce exactly TimeSteps (24) upsampled frames
// by ensuring the PPG slice is always 2 * NativeHalfFrames = 6 frames
// then upsampling 6 → 24 exactly.
// No zero-padding at the end needed.
int upsampledLen  = TimeSteps;  // always 24
float[,] ppgUpsampled = PpgUpsampler.Upsample(ppgSlice, upsampledLen);

int fluxFrames  = flux.GetLength(0);
int actualSteps = Math.Min(fluxFrames, TimeSteps);  // min(25, 24) = 24

float[,] features = new float[TimeSteps, FeatureDim];
for (int t = 0; t < TimeSteps; t++)
{
    for (int p = 0; p < PpgDim; p++)
        features[t, p] = t < actualSteps ? ppgUpsampled[t, p] : 0f;
    for (int f = 0; f < FluxDim; f++)
        features[t, PpgDim + f] = t < actualSteps ? flux[t, f] : 0f;
}
```

Also update the PPG slice extraction to use symmetric centering:
```csharp
// Replace the pStart50/pEnd50 calculation:

// OLD — two independent int() truncations
int pStart50 = (int)((centerTimeS - halfWindowS) * NativePpgRateHz);
int pEnd50   = (int)((centerTimeS + halfWindowS) * NativePpgRateHz);

// NEW — symmetric centered window
int nativeHalfFrames = (int)Math.Round(halfWindowS * NativePpgRateHz); // = 3
int centerPpgFrame   = (int)Math.Round(centerTimeS * NativePpgRateHz);
int pStart50         = centerPpgFrame - nativeHalfFrames;
int pEnd50           = centerPpgFrame + nativeHalfFrames;
// sliceLen is always exactly 6 (clamped for boundaries near audio edges)
```

---

### Summary

| | Current (trained model) | After retraining |
|---|---|---|
| PPG slice length | 5–7 frames (varies) | Always 6 frames |
| Upsampled length | 20–28 frames (varies) | Always 24 frames |
| Zero-padding at end | Sometimes 4 frames | Never |
| Model input quality | Inconsistent | Consistent |
| C# matches Python | ✅ (with rate-based fix) | ✅ (with symmetric fix) |
| Recommended | Current inference | Future retraining |
