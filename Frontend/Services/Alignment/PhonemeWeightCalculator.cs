namespace Frontend.Services.Alignment;

/// <summary>
/// Assigns a relative duration weight to a single phoneme token.
/// Vowels and silence = 1.0, everything else (consonants, unknown graphemes) = 0.3.
/// The vowel set covers IPA, ARPA, and basic roman vowel letters/diphthongs.
/// </summary>
public static class PhonemeWeightCalculator
{
    public const double VowelWeight     = 1.0;
    public const double ConsonantWeight = 0.3;

    private static readonly System.Collections.Generic.HashSet<string> VowelSet =
        new(System.StringComparer.OrdinalIgnoreCase)
    {
        // ── Silence / pause tokens ──────────────────────────────────────────
        "_", "sil", "sp", "spn",

        // ── IPA monophthongs ────────────────────────────────────────────────
        "a", "ä", "æ", "ɐ", "ɑ", "ɒ",
        "e", "ø", "ə", "ɘ", "ɵ", "ɞ",
        "ɛ", "œ", "ɜ", "ɝ",
        "i", "y", "ɨ", "ʉ",
        "ɪ", "ʏ",
        "o", "ɤ",
        "ɔ",
        "u", "ɯ", "ʊ",

        // ── IPA diphthongs ──────────────────────────────────────────────────
        "ai", "aɪ", "aʊ", "au",
        "eɪ", "ei",
        "oɪ", "ɔɪ",
        "oʊ", "ou",
        "ʊə", "ɪə", "eə",

        // ── IPA nasalized vowels ────────────────────────────────────────────
        "ã", "ẽ", "ĩ", "õ", "ũ",
        "ɑ̃", "ɛ̃", "ɔ̃", "œ̃",

        // ── ARPA monophthongs ───────────────────────────────────────────────
        "AA", "AE", "AH", "AO", "AW", "AX", "AXR",
        "EH", "ER", "IH", "IY",
        "OW", "OY",
        "UH", "UW",

        // ── ARPA diphthongs ─────────────────────────────────────────────────
        "AY", "EY",

        // ── ARPA with stress digits (0/1/2) ──────────────────────────────────
        "AA0","AA1","AA2", "AE0","AE1","AE2", "AH0","AH1","AH2",
        "AO0","AO1","AO2", "AW0","AW1","AW2",
        "AY0","AY1","AY2",
        "EH0","EH1","EH2", "ER0","ER1","ER2", "EY0","EY1","EY2",
        "IH0","IH1","IH2", "IY0","IY1","IY2",
        "OW0","OW1","OW2", "OY0","OY1","OY2",
        "UH0","UH1","UH2", "UW0","UW1","UW2",

        // ── Basic roman vowel letters ─────────────────────────────────────────
        "a", "e", "i", "o", "u",
        "A", "E", "I", "O", "U",
    };

    /// <summary>
    /// Returns the weight for a single token string.
    /// </summary>
    public static double GetWeight(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ConsonantWeight;

        return VowelSet.Contains(token) ? VowelWeight : ConsonantWeight;
    }

    /// <summary>
    /// Splits an interval's text into tokens and returns total weight.
    /// An empty or whitespace-only text yields a single ConsonantWeight unit
    /// so the interval still occupies proportional space.
    /// </summary>
    public static double GetTotalWeight(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ConsonantWeight;

        var tokens = text.Split(' ',
            System.StringSplitOptions.RemoveEmptyEntries |
            System.StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return ConsonantWeight;

        double total = 0.0;
        foreach (var t in tokens)
            total += GetWeight(t);

        return total;
    }
}
