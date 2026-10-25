using System.Windows;

namespace OpenPen.Services;

public sealed class OverlayManager : IDisposable
{
    private readonly AppController _controller;
    private readonly List<Views.OverlayWindow> _windows = new();
    private bool _disposed;

    public OverlayManager(AppController controller)
    {
        _controller = controller;
    }

    public void Rebuild()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var window in _windows)
        {
            window.Close();
        }

        _windows.Clear();

        foreach (var monitor in MonitorService.GetMonitors())
        {
            var window = new Views.OverlayWindow(_controller, monitor);
            _windows.Add(window);
        }
    }

    public void ApplyState()
    {
        foreach (var window in _windows)
        {
            window.ApplyState();
        }
    }

    public void UpdateSpotlight(Point physicalPoint)
    {
        foreach (var window in _windows)
        {
            window.UpdateSpotlight(physicalPoint);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var window in _windows)
        {
            window.Close();
        }

        _windows.Clear();
    }
}
