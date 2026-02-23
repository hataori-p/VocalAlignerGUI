# Technical Design Document (TDD)

## 1. System Architecture
The system is a **Monolithic Native Desktop Application** running on .NET 8.0.

*   **UI Layer:** Avalonia (MVVM pattern). Responsible for Rendering, User Interaction, and Visual State.
*   **Service Layer:** C# Services. Responsible for Orchestration, Audio Playback, Scripting, and File I/O.
*   **Core Engine:** High-performance computation. Responsible for DSP (FFT/Mel), ONNX Inference, and Alignment Algorithms (Viterbi).

## 2. Frontend Architecture (MVVM)

### Data Models (`Frontend/Models`)
*   **`TimelineState`**: Single source of truth for Viewport.
    *   Properties: `VisibleStartTime`, `VisibleEndTime`, `PlaybackPosition`, `PixelsPerSecond`.
    *   Methods: `SetView(start, end)`, `Zoom(factor)`.
*   **`TextGrid`**: Represents the transcription data.
    *   Contains `ObservableCollection<TextInterval>` and `ObservableCollection<AlignmentBoundary>`.
    *   *Constraint Logic:* Intervals share references to Boundaries. Dragging one boundary updates the adjacent intervals automatically.

### Services (`Frontend/Services`)
*   **`NativeAlignmentService`**: The primary bridge to the AI.
    *   Manages `RexEngine` (Acoustic Model) and `RefinerEngine` (Boundary Refinement).
    *   Handles caching of logits and PPGs to allow instant re-alignment without re-running the heavy acoustic model.
*   **`PhonemizerScriptingService`**: Wraps the `MoonSharp` Lua interpreter.
    *   Loads scripts from `lua/phonemizers/*.lua`.
    *   Exposes a sandboxed C# API (`vocal.io`, `vocal.text`) to Lua.
*   **`AudioPlayerService`**: Uses `NAudio`.
    *   Loads raw audio into memory for DSP processing.
    *   Manages playback cursor and timing.

## 3. Core Engine (`Frontend/Core`)

### DSP (`Frontend/Core/DSP`)
*   **`StftProcessor`**: Implements a specific Short-Time Fourier Transform configuration matching `librosa` (Hann window, Center padding).
*   **`MelFilterbank`**: Converts linear power spectrograms to Mel scale (Slaney normalization).
*   **`RefinementFeatureExtractor`**: Extracts (24, 41) feature windows (PPG + Spectral Flux) centered on boundaries for the Refiner model.

### Inference (`Frontend/Core/Inference`)
*   **`RexEngine`**: Wraps `Microsoft.ML.OnnxRuntime`.
    *   Input: 16kHz Audio.
    *   Output: Logits (Frame probabilities).
*   **`RefinerEngine`**: Wraps the secondary ONNX model.
    *   Input: Local features + Phoneme IDs.
    *   Output: Millisecond offset correction.

### Alignment (`Frontend/Core/Alignment`)
*   **`ViterbiAligner`**: A custom implementation of the Viterbi algorithm to find the optimal path of phonemes through the probability trellis.
*   **`ElasticAligner`**: A fallback algorithm for "Manual Mode" that distributes phonemes based on linguistic weights (Vowel vs. Consonant) without AI.

## 4. Key Algorithms

### "Smart Drag" (Assisted Mode)
When a user Alt-Drags a boundary in **Assisted Mode**:
1.  The boundary is conceptually merged with its neighbors (Left/Right anchors found).
2.  `NativeAlignmentService.AlignScopedAsync` is called with the specific time range and phoneme substring.
3.  The system performs Viterbi alignment on the cached logits for just that slice.
4.  The system extracts features for the new internal boundaries and runs the `RefinerEngine`.
5.  The UI updates effectively instantly.
