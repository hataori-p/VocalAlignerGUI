using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Frontend.ViewModels;

namespace Frontend.Controls;

public partial class TouchKeyboardOverlay : UserControl
{
    private bool _isDragging = false;
    private Point _dragStartPointer;
    private double _dragStartX;
    private double _dragStartY;

    public TouchKeyboardOverlay()
    {
        InitializeComponent();

        var dragBorder = this.FindControl<Border>("DragBorder");
        if (dragBorder != null)
        {
            dragBorder.PointerPressed += OnDragPressed;
            dragBorder.PointerMoved += OnDragMoved;
            dragBorder.PointerReleased += OnDragReleased;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is MainWindowViewModel vm && vm.OskX == 0 && vm.OskY == 0)
        {
            // Defer until layout is complete so we have valid bounds
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var root = GetRootVisual();
                if (root == null) return;

                double rootWidth  = root.Bounds.Width;
                double rootHeight = root.Bounds.Height;
                double selfWidth  = this.Bounds.Width;
                double selfHeight = this.Bounds.Height;

                const double bottomMargin = 20;

                vm.OskX = (rootWidth  - selfWidth)  / 2;
                vm.OskY =  rootHeight - selfHeight - bottomMargin;
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private Visual? GetRootVisual() =>
        this.Parent as Visual ?? this.VisualRoot as Visual;

    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is MainWindowViewModel vm)
        {
            _isDragging = true;
            _dragStartPointer = e.GetPosition(GetRootVisual());
            _dragStartX = vm.OskX;
            _dragStartY = vm.OskY;
            (sender as IInputElement)?.Focus();
            e.Pointer.Capture(this.FindControl<Border>("DragBorder"));
            e.Handled = true;
        }
    }

    private void OnDragMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || DataContext is not MainWindowViewModel vm) return;

        var currentPosition = e.GetPosition(GetRootVisual());
        var delta = currentPosition - _dragStartPointer;

        vm.OskX = _dragStartX + delta.X;
        vm.OskY = _dragStartY + delta.Y;

        e.Handled = true;
    }

    private void OnDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }
}
