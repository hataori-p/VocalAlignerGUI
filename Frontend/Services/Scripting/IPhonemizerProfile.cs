using System.Collections.Generic;
using System.Threading.Tasks;

namespace Frontend.Services.Scripting;

public enum PhonemeEncoding
{
    IPA,
    ARPABET,
    XSAMPA,
    Romaji,
    Custom
}

/// <summary>
/// Contract for all phonemizer profiles (Lua-backed or native C#).
/// Designed to replace Python server's PhonemizeListAsync,
/// GetPhonemizerProfilesAsync, and GetModelInventoryAsync.
/// </summary>
public interface IPhonemizerProfile
{
    /// <summary>Unique machine-readable ID. e.g. "ja_romaji_rex"</summary>
    string Id { get; }

    /// <summary>Human-readable name for UI dropdown.</summary>
    string DisplayName { get; }

    /// <summary>The phoneme encoding this profile outputs.</summary>
    PhonemeEncoding OutputEncoding { get; }

    /// <summary>
    /// Complete set of valid output symbols.
    /// Replaces GetModelInventoryAsync() — loaded once at startup.
    /// Used by ValidateGrid() for O(1) symbol checks.
    /// Empty set means "no validation" (pass-through).
    /// </summary>
    IReadOnlySet<string> SupportedSymbols { get; }

    /// <summary>
    /// Convert a list of text strings to phoneme sequences.
    /// Each input string maps to one output phoneme string (space-separated).
    /// e.g. inputs:  ["konnichiwa", "arigatou"]
    ///      outputs: ["k o n n i ch i w a", "a r i g a t o"]
    /// Replaces PhonemizeListAsync() from PythonBridgeService.
    /// </summary>
    Task<IReadOnlyList<string>> PhonemizeListAsync(IReadOnlyList<string> inputs);

    /// <summary>
    /// Optional: convert phoneme sequence back to grapheme text.
    /// Returns null if not supported or conversion fails.
    /// Used for post-alignment readability (IPA -> Romaji display).
    /// </summary>
    Task<string?> TryDePhonemizeAsync(IReadOnlyList<string> phonemes);

    /// <summary>
    /// Converts a flat sequence of phoneme symbols to normalized IPA.
    /// Input and output counts MUST match (count-preserving).
    /// Used by the Transcoder workflow (one phoneme per interval).
    /// Returns null if the profile does not support transcoding.
    /// </summary>
    Task<IReadOnlyList<string>?> TranscodeSequenceAsync(IReadOnlyList<string> phonemes);

    /// <summary>
    /// Optional: validate a single phoneme symbol against this profile's inventory.
    /// Default implementation: check SupportedSymbols (if non-empty).
    /// Override in Lua via profile.validate_symbol(sym) -> bool.
    /// </summary>
    bool ValidateSymbol(string symbol) =>
        SupportedSymbols.Count == 0 || SupportedSymbols.Contains(symbol);
}

public enum CompatibilityLevel
{
    Unknown,        // converter has no declared symbols
    Full,           // converter.SupportedSymbols ⊆ model.PhonemeSet
    Partial,        // non-empty intersection but not full subset
    Incompatible    // empty intersection
}

public interface IModelProfile
{
    bool IsManualMode { get; }
    string Id { get; }
    string DisplayName { get; }
    string ModelFile { get; }
    string? RefinerFile { get; }
    PhonemeEncoding Encoding { get; }
    IReadOnlySet<string> PhonemeSet { get; }

    /// <summary>
    /// Derive compatibility of a converter against this model's phoneme set.
    /// </summary>
    CompatibilityLevel ScoreCompatibility(IPhonemizerProfile converter)
    {
        if (converter.SupportedSymbols.Count == 0)
            return CompatibilityLevel.Unknown;

        bool allMatch = converter.SupportedSymbols.IsSubsetOf(PhonemeSet);
        if (allMatch) return CompatibilityLevel.Full;

        bool anyMatch = converter.SupportedSymbols.Overlaps(PhonemeSet);
        return anyMatch ? CompatibilityLevel.Partial : CompatibilityLevel.Incompatible;
    }
}
