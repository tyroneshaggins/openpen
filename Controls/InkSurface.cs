using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenPen.Models;
using OpenPen.Rendering;
using OpenPen.Services;

namespace OpenPen.Controls;

/// <summary>
/// A per-monitor drawing surface. Strokes live in virtual-desktop physical
/// coordinates, so the same drawing is shown correctly on every monitor.
/// </summary>
public sealed class InkSurface : FrameworkElement
{
    private enum DragMode
    {
        None,
        Draw,
        Erase
    }

    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _inkVisual = new();
    private readonly DrawingVisual _liveVisual = new();
    private readonly DrawingVisual _spotVisual = new();

    private AppController? _controller;
    private InkDocument? _document;
    private DragMode _dragMode;
    private Point _lastErasePhysical;
    private Point _pointerPhysical;
    private Point _spotlightPhysical;
    private bool _pointerInside;
    private bool _inkVisible = true;
    private bool _spotlight;
    private bool _interactive;
    private double _spotlightRadius = 170;
    private double _pixelsPerDip = 1.0;

    public Rect MonitorBounds { get; private set; }
    public double OriginX { get; private set; }
    public double OriginY { get; private set; }
    public double Scale { get; private set; } = 1.0;

    public InkSurface()
    {
        _visuals = new VisualCollection(this)
        {
            _inkVisual,
            _liveVisual,
            _spotVisual
        };

        Focusable = false;
        IsHitTestVisible = true;
        ClipToBounds = true;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        IsManipulationEnabled = false;
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    protected override Size MeasureOverride(Size availableSize) => availableSize;

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    public void Attach(AppController controller)
    {
        Detach();
        _controller = controller;
        _document = controller.Document;
        _document.Changed += OnDocumentChanged;
        _document.LiveChanged += OnLiveChanged;
        _document.FadeChanged += OnFadeChanged;
        _controller.StateChanged += OnStateChanged;

        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        ApplyState();
    }

    public void Detach()
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
            _document.LiveChanged -= OnLiveChanged;
            _document.FadeChanged -= OnFadeChanged;
        }

        if (_controller is not null)
        {
            _controller.StateChanged -= OnStateChanged;
        }

        _document = null;
        _controller = null;
    }

    public void SetMonitor(Rect physicalBounds, double scale)
    {
        MonitorBounds = physicalBounds;
        OriginX = physicalBounds.X;
        OriginY = physicalBounds.Y;
        Scale = scale <= 0.01 ? 1.0 : scale;
        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        RebuildAll();
    }

    public void ApplyState()
    {
        if (_controller is null)
        {
            return;
        }

        var state = _controller.State;
        _inkVisible = state.ShouldRenderInk;
        _spotlight = state.SpotlightEnabled;
        _spotlightRadius = state.SpotlightRadius;
        _interactive = state.OverlayIsInteractive;

        if (_interactive && state.ActiveTool.IsDrawingTool())
        {
            Cursor = Cursors.None;
            ForceCursor = true;
        }
        else
        {
            Cursor = Cursors.Arrow;
            ForceCursor = false;
        }

        IsHitTestVisible = _interactive;
        RebuildAll();
    }

    public void UpdateSpotlight(Point physicalPoint)
    {
        _spotlightPhysical = physicalPoint;
        RebuildSpot();
    }

    public void RebuildAll()
    {
        RebuildStatic();
        RebuildLive();
        RebuildSpot();
    }

    public void RebuildStatic()
    {
        using var dc = _inkVisual.RenderOpen();
        if (!_inkVisible || _document is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var localBounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var lifetime = _document.FadeLifetime;
        var now = DateTime.UtcNow;

        foreach (var stroke in _document.Strokes)
        {
            var bounds = stroke.GetBounds();
            if (bounds.IsEmpty)
            {
                continue;
            }

            var local = new Rect(
                (bounds.X - OriginX) / Scale,
                (bounds.Y - OriginY) / Scale,
                bounds.Width / Scale,
                bounds.Height / Scale);
            if (!local.IntersectsWith(localBounds))
            {
                continue;
            }

            var alpha = lifetime is { } life ? stroke.GetFadeAlpha(now, life) : 1.0;
            InkRenderer.DrawStroke(dc, stroke, GlobalToLocal, alpha, _pixelsPerDip);
        }
    }

    public void RebuildLive()
    {
        using var dc = _liveVisual.RenderOpen();
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        if (_document?.ActiveStroke is { } active && _inkVisible)
        {
            InkRenderer.DrawStroke(dc, active, GlobalToLocal, 1.0, _pixelsPerDip);
        }

        if (!_interactive || !_pointerInside || _spotlight || _controller is null)
        {
            return;
        }

        var state = _controller.State;
        var tool = state.ActiveTool;
        if (!tool.IsDrawingTool())
        {
            return;
        }

        var center = GlobalToLocal(_pointerPhysical);
        if (center.X < -24 || center.Y < -24 || center.X > ActualWidth + 24 || center.Y > ActualHeight + 24)
        {
            return;
        }

        if (tool is ToolKind.Text || tool.IsShape())
        {
            DrawCrosshair(dc, center);
            return;
        }

        var size = Math.Max(1, state.SizeForTool(tool));
        var radius = Math.Max(4, size * 0.5);
        var dark = new Pen(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), 3.0);
        var light = new Pen(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), 1.3);

        if (tool == ToolKind.Eraser)
        {
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), null, center, radius, radius);
        }

        dc.DrawEllipse(null, dark, center, radius, radius);
        dc.DrawEllipse(null, light, center, radius, radius);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), null, center, 1.4, 1.4);
    }

    public void RebuildSpot()
    {
        using var dc = _spotVisual.RenderOpen();
        if (!_spotlight || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var full = new Rect(0, 0, ActualWidth, ActualHeight);
        var dim = new SolidColorBrush(Color.FromArgb(166, 3, 5, 9));
        var cursorHere = MonitorBounds.Contains(_spotlightPhysical);

        if (!cursorHere)
        {
            dc.DrawRectangle(dim, null, full);
            return;
        }

        var local = GlobalToLocal(_spotlightPhysical);
        var radius = Math.Max(40, _spotlightRadius * 0.5);
        var hole = new EllipseGeometry(local, radius, radius);
        var mask = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(full), hole);
        dc.DrawGeometry(dim, null, mask);

        var softPen = new Pen(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)), 10);
        var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 1.7);
        var accentPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 122, 69)), 1.2);
        dc.DrawEllipse(null, softPen, local, radius + 3, radius + 3);
        dc.DrawEllipse(null, ringPen, local, radius, radius);
        dc.DrawEllipse(null, accentPen, local, radius + 7, radius + 7);
    }

    private void DrawCrosshair(DrawingContext dc, Point center)
    {
        var dark = new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 3.0);
        var light = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 1.2);
        const double arm = 7;

        dc.DrawLine(dark, new Point(center.X - arm, center.Y), new Point(center.X + arm, center.Y));
        dc.DrawLine(dark, new Point(center.X, center.Y - arm), new Point(center.X, center.Y + arm));
        dc.DrawLine(light, new Point(center.X - arm, center.Y), new Point(center.X + arm, center.Y));
        dc.DrawLine(light, new Point(center.X, center.Y - arm), new Point(center.X, center.Y + arm));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), null, center, 1.4, 1.4);
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        RebuildStatic();
        RebuildLive();
    }

    private void OnFadeChanged(object? sender, EventArgs e)
    {
        RebuildStatic();
    }

    private void OnLiveChanged(object? sender, EventArgs e)
    {
        RebuildLive();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        ApplyState();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _pointerInside = true;
        _pointerPhysical = LocalToGlobal(e.GetPosition(this));
        RebuildLive();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragMode == DragMode.None)
        {
            _pointerInside = false;
            RebuildLive();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_controller is null || _document is null)
        {
            return;
        }

        _pointerInside = true;
        _pointerPhysical = LocalToGlobal(e.GetPosition(this));
        _controller.RaiseToolbar();

        if (_dragMode == DragMode.Draw)
        {
            _document.UpdateActivePoint(ToStrokePoint(_pointerPhysical, 1.0), 1.0 * Scale);
        }
        else if (_dragMode == DragMode.Erase)
        {
            var radius = _controller.State.EraserSize * 0.5 * Scale;
            _document.EraseSegment(_lastErasePhysical, _pointerPhysical, radius);
            _lastErasePhysical = _pointerPhysical;
            RebuildLive();
        }
        else
        {
            RebuildLive();
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_controller is null || _document is null || !_interactive)
        {
            return;
        }

        var state = _controller.State;
        if (!state.ActiveTool.IsDrawingTool() || !_inkVisible)
        {
            return;
        }

        _pointerInside = true;
        _pointerPhysical = LocalToGlobal(e.GetPosition(this));
        _controller.RaiseToolbar();

        if (state.ActiveTool == ToolKind.Text)
        {
            _controller.OpenTextAt(_pointerPhysical);
            e.Handled = true;
            return;
        }

        if (state.ActiveTool == ToolKind.Eraser)
        {
            _dragMode = DragMode.Erase;
            CaptureMouse();
            _document.BeginErase();
            _lastErasePhysical = _pointerPhysical;
            _document.EraseSegment(_pointerPhysical, _pointerPhysical, state.EraserSize * 0.5 * Scale);
        }
        else
        {
            _dragMode = DragMode.Draw;
            CaptureMouse();
            _document.BeginStroke(
                state.ActiveTool,
                ColorUtil.WithAlpha(state.InkColor, state.InkOpacity),
                state.SizeForTool(state.ActiveTool),
                ToStrokePoint(_pointerPhysical, 1.0));
        }

        RebuildLive();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_controller is null || !_interactive || !_controller.State.ActiveTool.IsDrawingTool())
        {
            return;
        }

        _controller.AdjustSize(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        base.OnStylusDown(e);
        if (_controller is null || _document is null || !_interactive)
        {
            return;
        }

        var state = _controller.State;
        if (!state.ActiveTool.IsDrawingTool() || !_inkVisible)
        {
            return;
        }

        var points = e.GetStylusPoints(this);
        if (points.Count == 0)
        {
            return;
        }

        _pointerInside = true;
        _pointerPhysical = LocalToGlobal(points[points.Count - 1].ToPoint());
        _controller.RaiseToolbar();

        if (state.ActiveTool == ToolKind.Text)
        {
            _controller.OpenTextAt(_pointerPhysical);
            e.Handled = true;
            return;
        }

        var inverted = e.StylusDevice?.Inverted == true;
        if (state.ActiveTool == ToolKind.Eraser || inverted)
        {
            _dragMode = DragMode.Erase;
            CaptureStylus();
            _document.BeginErase();
            _lastErasePhysical = _pointerPhysical;
            var radius = state.EraserSize * 0.5 * Scale;
            _document.EraseSegment(_pointerPhysical, _pointerPhysical, radius);
        }
        else
        {
            _dragMode = DragMode.Draw;
            CaptureStylus();
            var first = points[0];
            var physical = LocalToGlobal(first.ToPoint());
            _document.BeginStroke(
                state.ActiveTool,
                ColorUtil.WithAlpha(state.InkColor, state.InkOpacity),
                state.SizeForTool(state.ActiveTool),
                ToStrokePoint(physical, first.PressureFactor));

            for (var i = 1; i < points.Count; i++)
            {
                var point = points[i];
                _document.UpdateActivePoint(ToStrokePoint(LocalToGlobal(point.ToPoint()), point.PressureFactor), 0.4 * Scale);
            }
        }

        RebuildLive();
        e.Handled = true;
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        base.OnStylusMove(e);
        if (_controller is null || _document is null || _dragMode == DragMode.None)
        {
            if (_controller is not null)
            {
                _pointerInside = true;
                _pointerPhysical = LocalToGlobal(e.GetPosition(this));
                RebuildLive();
            }

            return;
        }

        var points = e.GetStylusPoints(this);
        foreach (var point in points)
        {
            var physical = LocalToGlobal(point.ToPoint());
            _pointerPhysical = physical;

            if (_dragMode == DragMode.Erase)
            {
                var radius = _controller.State.EraserSize * 0.5 * Scale;
                _document.EraseSegment(_lastErasePhysical, physical, radius);
                _lastErasePhysical = physical;
            }
            else
            {
                _document.UpdateActivePoint(ToStrokePoint(physical, point.PressureFactor), 0.5 * Scale);
            }
        }

        RebuildLive();
        e.Handled = true;
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        base.OnStylusUp(e);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnStylusLeave(StylusEventArgs e)
    {
        base.OnStylusLeave(e);
        if (_dragMode == DragMode.None)
        {
            _pointerInside = false;
            RebuildLive();
        }
    }

    private void EndDrag()
    {
        if (_dragMode == DragMode.Draw)
        {
            _document?.EndStroke();
        }

        _dragMode = DragMode.None;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (IsStylusCaptured)
        {
            ReleaseStylusCapture();
        }

        RebuildLive();
    }

    private Point GlobalToLocal(Point physical) =>
        new((physical.X - OriginX) / Scale, (physical.Y - OriginY) / Scale);

    private Point LocalToGlobal(Point local) =>
        new(OriginX + local.X * Scale, OriginY + local.Y * Scale);

    private static StrokePoint ToStrokePoint(Point physical, double pressure) =>
        new(physical.X, physical.Y, pressure);

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        RebuildAll();
    }
}
