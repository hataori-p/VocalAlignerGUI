namespace Frontend.Models;

public class VadSegment
{
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public double Duration => EndTime - StartTime;
    public bool IsSpeech { get; set; }

    public VadSegment(double start, double end, bool isSpeech = true)
    {
        StartTime = start;
        EndTime = end;
        IsSpeech = isSpeech;
    }

    public override string ToString()
    {
        return $"[{StartTime:F3} - {EndTime:F3}] {(IsSpeech ? "Speech" : "Silence")}";
    }
}
