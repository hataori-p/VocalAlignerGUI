# Implementation Plan & Status

*Note: As of v0.1.1-alpha, the project architecture shifted from a Python Client-Server model to a 100% Native C# application using ONNX.*

## Phase 1: Scaffolding & Design (Completed)
- [x] Define Project Structure and Environment.
- [x] Define API Protocol (Superseded by Native Services).
- [x] Create Documentation (PRD, TDD).

## Phase 2: The Foundation (Completed)
- [x] Create `TimelineState` and `MainViewModel` for Zoom/Pan logic.
- [x] Create `WaveformControl` (Optimized Min/Max rendering).
- [x] Create `SpectrogramControl` (Local C# DSP via `StftProcessor` and `WriteableBitmap`).
- [x] Implement Audio Playback service (NAudio).

## Phase 3: The Editor Core (Completed)
- [x] Create `TextGrid`, `TextInterval`, and `AlignmentBoundary` data models.
- [x] Implement `TierControl` (Rendering intervals, text, and hit-testing).
- [x] Implement Mouse Interaction (Split, Merge, Rename, Selection, Hover states).
- [x] Integrate Lua Scripting Engine (MoonSharp) for extensible G2P phonemization.

## Phase 4: The Alignment Logic (Completed)
- [x] Migrate Python Allosaurus-Rex logic to native C# ONNX (`RexEngine`).
- [x] Implement Viterbi alignment and state-machine logic locally.
- [x] Implement "Smart Drag" logic in `TierControl` (Alt+Drag).
- [x] Wire up the local `AlignScopedAsync` loop for instant boundary refinement.
- [x] Implement `RefinerEngine` (Secondary ONNX model) for millisecond-accurate boundary shifts.

## Phase 5: Refinement (Current / Ongoing)
- [x] Implement Manual Mode / Elastic Aligner (Split/Merge/Distribute without AI).
- [x] Add PPG Guidance Lane (`RecognitionControl`).
- [x] Polish UI (Colors, Shortcuts, Find/Search Overlay).
- [ ] Add support for additional acoustic models (e.g., English, multi-lingual).
- [ ] UI Theme system (Dark/Light mode polish).
