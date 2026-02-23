using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Frontend.ViewModels;

namespace Frontend.Controls;

public partial class FindOverlay : UserControl
{
    private TextBox? _searchBox;

    public FindOverlay()
    {
        InitializeComponent();

        _searchBox = this.FindControl<TextBox>("SearchBox");
        if (_searchBox != null)
        {
            _searchBox.KeyDown += SearchBox_KeyDown;
            _searchBox.Bind(TextBox.TextProperty, new Binding("SearchQuery") { Mode = BindingMode.TwoWay });

            // Delay focus to ensure it works when visibility toggles and select all text.
            this.PropertyChanged += (s, e) =>
            {
                if (e.Property == IsVisibleProperty && IsVisible)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _searchBox.Focus();
                        _searchBox.SelectAll();
                    }, DispatcherPriority.Input);
                }
            };
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        // REMOVED: Spacebar hack (Handled globally in MainWindow now)

        if (e.Key != Key.Enter || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            vm.FindPreviousCommand.Execute(null);
        }
        else
        {
            vm.FindNextCommand.Execute(null);
        }

        e.Handled = true;
    }

    // --- FOCUS MANAGEMENT ---
    public void RefocusSearchBox(object? sender, RoutedEventArgs e)
    {
        _searchBox?.Focus();
    }
}
