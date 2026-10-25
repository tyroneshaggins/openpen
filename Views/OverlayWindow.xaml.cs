using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenPen.Controls;
using OpenPen.Services;

namespace OpenPen.Views;

public partial class OverlayWindow : Window
{
    private readonly AppController _controller;
    private readonly MonitorInfo _monitor;
    private bool _disposed;

    public OverlayWindow(AppController controller, MonitorInfo monitor)
    {
        _controller = controller;
        _monitor = monitor;
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        DpiChanged += OnDpiChanged;
        Closed += OnClosed;

        PreviewMouseDown += (_, _) => _controller.RaiseToolbar();
        PreviewMouseUp += (_, _) => _controller.RaiseToolbar();
        PreviewStylusDown += (_, _) => _controller.RaiseToolbar();
        PreviewStylusUp += (_, _) => _controller.RaiseToolbar();

        Show();
        UpdateGeometry();
        Surface.SetMonitor(_monitor.Bounds, MonitorService.GetDpiScaleAt(_monitor.Center));
        Surface.Attach(_controller);
        ApplyState();
    }

    public void ApplyState()
    {
        if (_disposed)
        {
            return;
        }

        var state = _controller.State;
        var shouldShow = state.OverlayShouldBeVisible;

        // A layered window is hit-tested by its alpha channel. A fully transparent
        // background would let every click fall through, so when the overlay is
        // interactive we paint it with 1/255 alpha: invisible, but solid to the mouse.
        Background = state.Whiteboard switch
        {
            Models.WhiteboardMode.White => new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xF7)),
            Models.WhiteboardMode.Black => new SolidColorBrush(Color.FromRgb(0x0D, 0x0F, 0x13)),
            _ => state.OverlayIsInteractive
                ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
                : Brushes.Transparent
        };

        if (shouldShow)
        {
            if (!IsVisible)
            {
                Show();
                WindowHelper.PlacePhysical(this, _monitor.Bounds);
            }
        }
        else if (IsVisible)
        {
            Hide();
        }

        WindowHelper.SetClickThrough(this, !state.OverlayIsInteractive);
        Surface.ApplyState();
    }

    public void UpdateSpotlight(Point physicalPoint)
    {
        Surface.UpdateSpotlight(physicalPoint);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.ApplyNoActivate(this);
        UpdateGeometry();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateGeometry();
        Surface.SetMonitor(_monitor.Bounds, MonitorService.GetDpiScaleAt(_monitor.Center));
        Surface.ApplyState();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        UpdateGeometry();
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateGeometry();
            Surface.SetMonitor(_monitor.Bounds, MonitorService.GetDpiScaleAt(_monitor.Center));
            Surface.RebuildAll();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateGeometry()
    {
        WindowHelper.PlacePhysical(this, _monitor.Bounds);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _disposed = true;
        Surface.Detach();
    }
}
