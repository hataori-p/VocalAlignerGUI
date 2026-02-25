# VocalAlignerGUI (v0.1.1-alpha)

A standalone forced alignment tool and TextGrid editor. It is designed specifically to help create and refine phonetic datasets for Singing Voice Synthesis (SVS) and neural vocoders. 

VocalAlignerGUI is built natively in C# (Avalonia) and uses the ONNX runtime for local inference. **No Python environment or background server is required.**

## Features

*   **Native AI Alignment:** Uses a two-stage ONNX pipeline (coarse timing + boundary refinement) for accurate forced alignment. *Note: For this initial alpha, only a Japanese acoustic model is provided.*
*   **Assisted & Manual Editing:** 
    *   Use the AI to roughly align phonemes, then pin specific boundaries as "Anchors" (Alt+Drag) to let the system automatically readjust the fluid phonemes between them.
    *   Alternatively, work entirely without AI models using a "Manual" profile to split, merge, and tweak TextGrids by hand.
*   **Lua Scripting Engine:** Built-in Lua support (via MoonSharp) handles G2P phonemization (e.g., Japanese Romaji/Kana to IPA) without needing to recompile the app.
*   **Optimized Visualization:** Local C# DSP implementation renders the spectrogram and waveform cleanly, even for longer audio files.

## Experimental: Custom Phonemizers (Lua)

VocalAlignerGUI includes a programmable scripting engine (via MoonSharp) that allows advanced users to add support for new languages or custom phonetic dictionaries without recompiling the application.

*   **How it works:** Native C# handles the heavy DSP and AI lifting, while Lua handles the Text-to-Phoneme (G2P) logic.
*   **Get Started:** See the [LUA_GUIDE.md](docs/LUA_GUIDE.md) in the `docs/` folder for a full prompt template you can give to an AI assistant (like Claude or ChatGPT) to generate a custom phonemizer for your specific language.
*   **Contributions:** If you create a high-quality phonemizer for a language not currently supported, please consider submitting a Pull Request!

### ✨ Support & Community

* **Technical Assistant (AI Chatbot):** Need help with VocalAlignerGUI?  
  → Try the official [VocalAlignerGUI Support Chat](https://vocal-aligner-chat.hataori.workers.dev/).  
  This lightweight web interface provides fast, context-aware answers about the app, phonetics, alignment workflows, and troubleshooting. It uses Cloudflare Turnstile for security and OpenRouter for inference — **no messages are stored**.

* **Report Issues & Feature Requests:**  
  → Open an issue on [GitHub Issues](https://github.com/hataori-p/VocalAlignerGUI/issues). Include logs (enable via *Help > Toggle File Logging*).

* **Privacy Note:** Messages are sent to OpenRouter (via Cloudflare Workers) for processing. Do **not** include private, sensitive, or copyrighted material.

## Installation & Setup

### For Users (Release Version)
1. Download the latest `v0.1.1-alpha` `.zip` from the Releases page.
2. Extract the folder to your desired location.
3. Ensure the required acoustic models (`.onnx` and `.yaml` files) are placed in the `Frontend/resources/models/` directory.
4. Run the `VocalAlignerGUI` executable.

### For Developers (Building from Source)
You will need the **.NET 8.0 SDK**.
```bash
git clone https://github.com/hataori-p/VocalAlignerGUI.git
cd VocalAlignerGUI
dotnet run --project Frontend/Frontend.csproj
```

## Basic Workflow

1. **Load Audio:** Open your `.wav` file (`File > Open Audio...`).
2. **Import Text:** Import a raw text file of your lyrics (`File > Import Text...`). 
3. **Phonemize:** Select a profile from dropdown (*Allosaurus REX*), choose a phonemizer script from the dropdown in Tools (*Japanese Transcriber*), and run the conversion tool. You must have black IPA phonemes separeted by spaces, not red text.
4. **Align:** Click **Realign** to snap the phonemes to the audio using the ONNX model.
5. **Adjust & Save:** Fix any misalignments manually, lock correct boundaries, and save your work as a `.TextGrid`.

## Notes for Alpha Testers

This is a **v0.1.1-alpha** release aimed at a niche group of dataset creators. You will likely encounter bugs, missing quality-of-life features, or UI quirks. 

If you run into issues, please open an issue on GitHub. To help speed up fixes, please include:
* A brief description of what you were doing.
* Screenshots of the UI (if applicable).
* The application logs (first enable them, will be in the app directory - use "Open Log File Location").

## Project Structure Overview

*   **`Frontend/`**: The core Avalonia C# app. Handles native DSP, ONNX inference (`RexEngine`, `RefinerEngine`), and the UI.
*   **`lua/`**: Lua scripts for text-to-phoneme conversion and mapping tables.
*   **`utils/`**: C# and Python validation scripts used during development.

*(Note: The actual training and command-line code for the acoustic models, "Allosaurus Rex", is a separate Python project that will be published at a later date.)*

## Acknowledgments & License

This project was built from the ground up with significant help from AI coding assistants (specifically Aider and various LLMs). Because of how much these open-source tools and models helped make this possible, this software is given back to the community under the **MIT License**. 

Contributions, forks, and pull requests are welcome!

> **from Hataori:**\
> (this paragraph is written by me, not AI)\
> I woudn't be able to create this project without AI.\
> So please do not hate me for it.\
> I will do it anyway
