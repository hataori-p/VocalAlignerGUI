namespace Frontend.Services.Alignment;

using Frontend.Models;
using System.Threading.Tasks;

public interface IAligner
{
    /// <summary>
    /// Aligns the provided text to the audio file.
    /// </summary>
    /// <param name="text">The full content text.</param>
    /// <param name="audioPath">Path to the .wav file.</param>
    /// <returns>A populated TextGrid.</returns>
    Task<TextGrid> AlignAsync(string text, string audioPath);
}
