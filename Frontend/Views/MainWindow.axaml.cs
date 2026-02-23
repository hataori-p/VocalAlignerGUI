using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Frontend.ViewModels;

namespace Frontend.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        // Load state synchronously so OnOpened can read IsMaximized immediately
        Frontend.Services.AppStateService.Load();
        InitializeComponent();
    }

    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);

        var state = Frontend.Services.AppStateService.Current;

        if (state.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        else if (state.WindowWidth > 100 && state.WindowHeight > 100)
        {
            Position = new PixelPoint((int)state.WindowX, (int)state.WindowY);
            Width    = state.WindowWidth;
            Height   = state.WindowHeight;
        }
    }

    private static string ShortenPath(string path, int maxLength = 55)
    {
        if (path.Length <= maxLength) return path;
        return "..." + path[^(maxLength - 3)..];
    }

    private async Task<string> ShowSavePromptAsync(string? currentPath)
    {
        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 400,
            Height = string.IsNullOrEmpty(currentPath) ? 160 : 185,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        string result = "cancel";

        bool hasPath = !string.IsNullOrEmpty(currentPath);

        var saveBtn    = new Button { Content = "Save",        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, IsVisible = hasPath };
        var saveAsBtn  = new Button { Content = "Save As...",  HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var discardBtn = new Button { Content = "Don't Save",  HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var cancelBtn  = new Button { Content = "Cancel",      HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };

        saveBtn.Click    += (_, __) => { result = "save";    dialog.Close(); };
        saveAsBtn.Click  += (_, __) => { result = "saveas";  dialog.Close(); };
        discardBtn.Click += (_, __) => { result = "discard"; dialog.Close(); };
        cancelBtn.Click  += (_, __) => { result = "cancel";  dialog.Close(); };

        // Build column definitions dynamically
        // 4 columns if hasPath (Save, SaveAs, Discard, Cancel), 3 if not
        var colDefs = hasPath ? new ColumnDefinitions("*,*,*,*") : new ColumnDefinitions("*,*,*");
        var buttonGrid = new Grid { ColumnDefinitions = colDefs };

        if (hasPath)
        {
            buttonGrid.Children.Add(saveBtn);
            buttonGrid.Children.Add(saveAsBtn);
            buttonGrid.Children.Add(discardBtn);
            buttonGrid.Children.Add(cancelBtn);
            Grid.SetColumn(saveBtn,    0);
            Grid.SetColumn(saveAsBtn,  1);
            Grid.SetColumn(discardBtn, 2);
            Grid.SetColumn(cancelBtn,  3);
        }
        else
        {
            buttonGrid.Children.Add(saveAsBtn);
            buttonGrid.Children.Add(discardBtn);
            buttonGrid.Children.Add(cancelBtn);
            Grid.SetColumn(saveAsBtn,  0);
            Grid.SetColumn(discardBtn, 1);
            Grid.SetColumn(cancelBtn,  2);
        }

        var stackPanel = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 12,
        };

        stackPanel.Children.Add(new TextBlock
        {
            Text = "You have unsaved changes. Save before closing?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        if (hasPath)
        {
            stackPanel.Children.Add(new TextBlock
            {
                Text = ShortenPath(currentPath!),
                FontStyle = Avalonia.Media.FontStyle.Italic,
                Opacity = 0.7,
                TextTrimming = Avalonia.Media.TextTrimming.None,
            });
        }

        stackPanel.Children.Add(buttonGrid);
        dialog.Content = stackPanel;

        await dialog.ShowDialog(this);
        return result;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        var vm = DataContext as MainWindowViewModel;

        if (vm != null && vm.IsDirty)
        {
            e.Cancel = true;
            base.OnClosing(e);

            string choice = await ShowSavePromptAsync(vm.CurrentTextGridPath);

            if (choice == "cancel")
                return;

            if (choice == "save" || choice == "saveas")
            {
                bool saved = false;

                if (choice == "save" && !string.IsNullOrEmpty(vm.CurrentTextGridPath))
                {
                    // In-place save
                    await vm.SaveTextGridCommand.ExecuteAsync(null);
                    saved = !vm.IsDirty;
                }
                else
                {
                    // Save As picker
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel != null)
                    {
                        var startDir = vm.LastUsedDir;
                        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                        {
                            Title = "Save TextGrid As",
                            DefaultExtension = ".TextGrid",
                            SuggestedStartLocation = startDir != null
                                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(startDir)
                                : null,
                            FileTypeChoices = new[]
                            {
                                new FilePickerFileType("TextGrid") { Patterns = new[] { "*.TextGrid" } }
                            }
                        });

                        if (file != null)
                        {
                            await vm.SaveTextGridAs(file.Path.LocalPath);
                            saved = !vm.IsDirty;
                        }
                        else
                        {
                            // User dismissed Save As picker — treat as cancel
                            return;
                        }
                    }
                }

                if (!saved)
                    return; // Save failed — stay open
            }

            // "discard" OR save succeeded — save AppState and close
            var state = Frontend.Services.AppStateService.Current;
            state.IsMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                state.WindowX      = Position.X;
                state.WindowY      = Position.Y;
                state.WindowWidth  = Width;
                state.WindowHeight = Height;
            }
            vm.SaveTimelineState();

            vm.IsDirty = false;
            Close();
            return;
        }

        // Not dirty — normal close path
        var stateClean = Frontend.Services.AppStateService.Current;
        stateClean.IsMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            stateClean.WindowX      = Position.X;
            stateClean.WindowY      = Position.Y;
            stateClean.WindowWidth  = Width;
            stateClean.WindowHeight = Height;
        }
        vm?.SaveTimelineState();

        base.OnClosing(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Source is TextBox)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && DataContext is MainWindowViewModel vm)
        {
            if (e.Key == Key.Space)
            {
                vm.TogglePlaybackCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Z)
            {
                vm.ZoomToSelectionCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    private async void OnLoadAudioClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var vm = DataContext as MainWindowViewModel;
        var startDir = vm?.LastUsedDir;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Audio File",
            AllowMultiple = false,
            SuggestedStartLocation = startDir != null
                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(startDir)
                : null,
            FileTypeFilter = new[] { new FilePickerFileType("Audio Files") { Patterns = new[] { "*.wav", "*.mp3" } } }
        });

        // 3. Pass result to ViewModel with Progress Dialog
        if (files.Count >= 1 && vm != null)
        {
            var path = files[0].Path.LocalPath;
            {
                var progressDialog = new ProgressDialog("Loading Audio & Model...");
                try
                {
                    progressDialog.Show(this);
                    progressDialog.Activate();

                    // Force a UI update so the dialog has time to render before work begins.
                    await System.Threading.Tasks.Task.Delay(250);

                    await vm.LoadAudioCommand.ExecuteAsync(path);
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LOAD ERROR: {ex}");
                    vm.StatusMessage = $"Error: {ex.Message}";
                }
                finally
                {
                    try { progressDialog.Close(); } catch { }
                }
            }
        }
    }

    private async void OnImportTextClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var vm = DataContext as MainWindowViewModel;
        var startDir = vm?.LastUsedDir;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Plain Text",
            AllowMultiple = false,
            SuggestedStartLocation = startDir != null
                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(startDir)
                : null,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Text Files") { Patterns = new[] { "*.txt" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count >= 1 && vm != null)
        {
            var path = files[0].Path.LocalPath;
            await vm.ImportTextCommand.ExecuteAsync(path);
        }
    }

    private async void OnLoadTextGridClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var vm = DataContext as MainWindowViewModel;
        var startDir = vm?.LastUsedDir;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open TextGrid File",
            AllowMultiple = false,
            SuggestedStartLocation = startDir != null
                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(startDir)
                : null,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("TextGrid Files") { Patterns = new[] { "*.TextGrid" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count >= 1 && vm != null)
        {
            var path = files[0].Path.LocalPath;
            await vm.LoadTextGridCommand.ExecuteAsync(path);
        }
    }

    private async void OnLoadTextGridTierClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var vm = DataContext as MainWindowViewModel;
        var startDir = vm?.LastUsedDir;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open TextGrid File (Select Tier)",
            AllowMultiple = false,
            SuggestedStartLocation = startDir != null
                ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(startDir)
                : null,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("TextGrid Files") { Patterns = new[] { "*.TextGrid" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count >= 1 && vm != null)
        {
            var path = files[0].Path.LocalPath;
            await vm.LoadTextGridTierCommand.ExecuteAsync(path);
        }
    }

    private void OnOpenLogFolderClick(object? sender, RoutedEventArgs e)
    {
        var path = AppDomain.CurrentDomain.BaseDirectory;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private async void OnSaveTextGridAsClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        if (DataContext is MainWindowViewModel vm)
        {
            var startDir = vm.LastUsedDir;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save TextGrid",
                DefaultExtension = ".TextGrid",
                SuggestedStartLocation = startDir != null
                    ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(startDir)
                    : null,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("TextGrid") { Patterns = new[] { "*.TextGrid" } }
                }
            });

            if (file != null)
            {
                await vm.SaveTextGridAs(file.Path.LocalPath);
            }
        }
    }


    private async void OnRealignClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var progressDialog = new ProgressDialog("Realigning...");

        try
        {
            progressDialog.Show(this);
            await System.Threading.Tasks.Task.Delay(100);

            await vm.RealignCommand.ExecuteAsync(null);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ALIGN ERROR: {ex}");
            vm.StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            try { progressDialog.Close(); } catch { }
        }
    }

    private void OnAudioTransportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Visual visual) return;
        var point = e.GetPosition(visual);
        if (DataContext is MainWindowViewModel vm)
        {
            double time = vm.Timeline.XToTime(point.X);
            vm.SeekToCommand.Execute(time);
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
