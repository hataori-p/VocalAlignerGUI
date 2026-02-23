using CommunityToolkit.Mvvm.ComponentModel;

namespace Frontend.Models;

public partial class AlignmentBoundary : ObservableObject
{
    [ObservableProperty]
    private double _time;
    [ObservableProperty]
    private bool _isLocked; 
    [ObservableProperty]
    private bool _isSelected;
}
