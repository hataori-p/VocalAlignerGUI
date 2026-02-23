using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Frontend.Views;

public partial class ProgressDialog : Window
{
    public ProgressDialog()
    {
        InitializeComponent();
    }

    public ProgressDialog(string message) : this()
    {
        var textBlock = this.FindControl<TextBlock>("MessageText");
        if (textBlock != null)
        {
            textBlock.Text = message;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
