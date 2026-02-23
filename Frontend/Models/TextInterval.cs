using CommunityToolkit.Mvvm.ComponentModel;

namespace Frontend.Models;

public partial class TextInterval : ObservableObject
{
    // Note: Not observable properties because the Boundary objects themselves trigger updates
    public AlignmentBoundary Start { get; set; } = new();
    public AlignmentBoundary End { get; set; } = new();
    
    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isValid = true;

    public double Duration => End.Time - Start.Time;
}
