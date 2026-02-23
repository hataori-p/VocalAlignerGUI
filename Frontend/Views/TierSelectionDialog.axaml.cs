using Avalonia.Controls;
using Avalonia.Interactivity;
using Frontend.ViewModels;

namespace Frontend.Views;

public partial class TierSelectionDialog : Window
{
    public TierSelectionDialog()
    {
        InitializeComponent();
    }

    public TierSelectionDialog(TierSelectionViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TierSelectionViewModel vm && vm.SelectedTier != null)
        {
            Close(vm.SelectedTier);
        }
        else
        {
            Close(null);
        }
    }
}
