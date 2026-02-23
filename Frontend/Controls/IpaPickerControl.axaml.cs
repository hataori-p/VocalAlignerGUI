using Avalonia.Controls;
using Avalonia.Interactivity;
using Frontend.Models;
using System;
using System.Collections.Generic;

namespace Frontend.Controls;

public partial class IpaPickerControl : UserControl
{
    public event Action<string>? CharacterChosen;

    public IpaPickerControl()
    {
        InitializeComponent();
        Populate();
    }

    private void Populate()
    {
        var list = new List<string>();
        int rows = IpaProvider.Characters.GetLength(0);
        int cols = IpaProvider.Characters.GetLength(1);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                list.Add(IpaProvider.Characters[r, c]);
            }
        }
        
        var itemsControl = this.FindControl<ItemsControl>("GridItems");
        if(itemsControl != null) itemsControl.ItemsSource = list;
    }

    private void OnCharClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string charStr)
        {
            CharacterChosen?.Invoke(charStr);
        }
    }
}
