# API Communication Protocol (DEPRECATED)

**Status:** DEPRECATED (v0.1.1-alpha)
**Superseded By:** Internal C# Interfaces (`NativeAlignmentService`, `RexEngine`)

**Historical Context:**
In pre-alpha versions (v0.0.x), the application used a Python Flask server (`Backend/`) to handle DSP and Inference. As of v0.1.1, the entire pipeline has been ported to native C# using ONNX Runtime and custom DSP implementations. 

No HTTP or TCP communication occurs in the current version. This file is retained solely for archival purposes regarding the original Python backend design.

---

## Legacy Protocol Definition

### 1. Session Management
*   `POST /load`: Initialize audio session.

### 2. Visualization
*   `POST /spectrogram`: Request base64 image tiles. (Replaced by local `SpectrogramControl` using `WriteableBitmap`).

### 3. Alignment
*   `POST /align`: Request timestamp constraints. (Replaced by `NativeAlignmentService.AlignAsync`).
    
