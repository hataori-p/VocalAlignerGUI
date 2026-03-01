# Changelog

## [v0.1.2-alpha] - 2026-03-01

### New Features
*   **Touch & On-Screen Keyboard (OSK):** Added a floating, draggable on-screen keyboard to support touch-screen devices and tablets.
    *   Includes virtual "Sticky" modifiers (Ctrl, Alt, Shift).
    *   Added dedicated buttons for common actions: *Play Visible*, *Play Interval*, *Zoom Selection*, and *Delete Boundary*.
*   **In-App Controls Guide:** Added a new "Show Controls" panel in the Help menu that renders the controls documentation directly inside the app.
*   **New Playback Commands:** Added logic to "Play to End of View" and "Play Interval at Playhead" for faster auditing.

### 🇺🇸 English Language Support
*   **English Phonemizers:** Added a comprehensive English G2P (Grapheme-to-Phoneme) system.
    *   **Hybrid Engine:** Uses CMU Dictionary for known words and NRL Rules for unknown words.
    *   **Converters:** Added `ARPABET <-> IPA` converters.
*   **Manual English Profile:** Added a "Manual — English IPA" profile for elastic alignment without requiring an ONNX model.

### Fixes & Improvements
*   **Crash Fix:** Fixed a critical bug where the VAD and Smart Aligner tried to write logs to restricted system folders (e.g., Program Files), causing crashes. Logs now route correctly through the centralized `AppLogger`.
*   **Path Fixes:** Removed hardcoded local file paths that caused issues on first load.
*   **Cleanup:** Completely removed the deprecated Python backend code and legacy API models to reduce file size and confusion.
*   **License:** Added MIT License file.

### Documentation
*   Updated `README.md` with links to the new AI Support Chatbot.
*   Updated `CONTROLS.md` to include touch gestures and OSK interactions.
