using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Frontend.Models;

namespace Frontend.ViewModels;

public partial class TierSelectionViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<string> _tierNames = new();

    [ObservableProperty]
    private string? _selectedTierName;

    private readonly List<TextGrid> _tiers;

    public TierSelectionViewModel(List<TextGrid> tiers)
    {
        _tiers = tiers;
        foreach (var tier in tiers)
        {
            TierNames.Add(tier.Name);
        }
        SelectedTierName = TierNames.FirstOrDefault();
    }

    public TextGrid? SelectedTier => string.IsNullOrEmpty(SelectedTierName) 
        ? null : _tiers.FirstOrDefault(t => t.Name == SelectedTierName);
}
