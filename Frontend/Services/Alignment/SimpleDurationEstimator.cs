namespace Frontend.Services.Alignment;

using System.Linq;
using System.Text.RegularExpressions;

public class SimpleDurationEstimator
{
    private static readonly Regex VowelRegex = new Regex("[aeiouy\u3040-\u30ff]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Estimates the relative duration weight of a text string based on vowel count.
    /// </summary>
    public double EstimateWeight(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        // Count vowels (English + basic Kana support overlap in regex)
        int vowels = VowelRegex.Matches(text).Count;

        // Fallback for silence tokens or abbreviations
        if (vowels == 0)
        {
            // If it's punctuation, very small weight
            if (text.All(c => !char.IsLetterOrDigit(c))) return 0.0;
            
            // Otherwise assume roughly 0.3 vowels per char length
            return text.Length * 0.3;
        }

        return vowels;
    }
}
