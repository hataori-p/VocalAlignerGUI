# Product Requirements Document (PRD)

## 1. Project Vision
VocalAlignerGUI is a professional-grade, cross-platform desktop tool for forcing synchronization between audio and text (Phonemes/Words). It serves as a dedicated "transcription workflow" tool for creating high-quality datasets for Neural Vocoders and Singing Voice Synthesis (SVS). 

The application is built as a **standalone, high-performance native desktop application**. It requires no background servers, no Python environments, and performs all DSP and AI inference locally using C# and the ONNX runtime.

## 2. Core Workflows

### A. The Two-Mode Editing Strategy
The application operates in two distinct modes depending on the loaded profile:

1.  **Assisted Mode (AI Coarse & Fine Alignment)**
    *   **Goal:** Establish "Hard Constraints" (Anchors) to guide the AI, which automatically aligns the fluid segments in between.
    *   **Visuals:** Fluid boundaries are **Blue**; Locked Anchors are **Red**.
    *   **Interaction:**
        *   Dragging a Blue line moves it temporarily.
        *   Double-clicking a Blue line locks it (turns **Red**).
        *   Alt-Dragging a boundary executes a "Smart Drag", merging neighboring boundaries and triggering a scoped AI re-alignment between the nearest anchors.
    *   **Guidance:** A "Phoneme Lane" displays raw AI predictions to help the user identify audio features.

2.  **Manual Mode (Elastic / Fine Tuning)**
    *   **Goal:** Pixel-perfect correction of phoneme boundaries without AI intervention.
    *   **Interaction:** Dragging boundaries acts elastically, proportionally distributing the space of internal phonemes based on natural vowel/consonant weighting.

### B. Extensible Phonemization
*   **Users must convert raw lyrics (e.g., Japanese Romaji or Kana) into Stable IPA prior to alignment.**
*   This logic is entirely separated from the compiled codebase, using user-editable **Lua scripts** for instant customization of G2P (Grapheme-to-Phoneme) rules.

### C. Visualization
*   **Spectrogram:** High-performance, locally computed (STFT + Mel Filterbank) rendering, dynamic range adjustable.
*   **Waveform:** Min/Max pyramid rendering for instant zoom on long files.
*   **Timeline:** Unified zoom/scroll for all lanes with millisecond precision.

## 3. Functional Requirements

### Frontend & Core Engine (C# Avalonia)
*   **FR-01:** Must load WAV files and compute Waveform/Spectrogram instantly via local native DSP.
*   **FR-02:** Must render `TextGrid` layers (Intervals) with sub-pixel precision.
*   **FR-03:** Must play audio from the cursor position, selected ranges, or specific intervals.
*   **FR-04:** Must execute the `Allosaurus-Rex` acoustic models natively using `Microsoft.ML.OnnxRuntime`.
*   **FR-05:** Must support a two-stage alignment process (Coarse Viterbi alignment + Feature-based Boundary Refinement).
*   **FR-06:** Must include an embedded Lua runtime (`MoonSharp`) to hot-load phonemizer scripts and model manifests from the `lua/` directory.
*   **FR-07:** Must support "Anchor" logic: maintaining a list of locked times vs. fluid times and facilitating "Smart Drag" realignment operations.
