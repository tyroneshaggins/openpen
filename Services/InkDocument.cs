using System.Windows;
using System.Windows.Media;
using OpenPen.Models;

namespace OpenPen.Services;

/// <summary>
/// Holds the annotation strokes in virtual-desktop physical pixels.
/// Undo/redo is snapshot based: quick, predictable, and tiny for normal use.
/// </summary>
public sealed class InkDocument
{
    private readonly List<Stroke> _strokes = new();
    private readonly Stack<List<Stroke>> _undo = new();
    private readonly Stack<List<Stroke>> _redo = new();
    private bool _pendingUndo;
    private bool _eraseUndoPushed;
    private const int MaxUndo = 80;

    public event EventHandler? Changed;
    public event EventHandler? LiveChanged;
    public event EventHandler? FadeChanged;

    public IReadOnlyList<Stroke> Strokes => _strokes;
    public Stroke? ActiveStroke { get; private set; }
    public TimeSpan? FadeLifetime { get; set; }

    public bool HasStrokes => _strokes.Count > 0;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public Stroke BeginStroke(ToolKind tool, Color color, double width, StrokePoint start)
    {
        CancelActiveStroke(silent: true);

        var kind = tool switch
        {
            ToolKind.Pen or ToolKind.Highlighter => StrokeKind.Freehand,
            ToolKind.Line => StrokeKind.Line,
            ToolKind.Arrow => StrokeKind.Arrow,
            ToolKind.Rectangle => StrokeKind.Rectangle,
            ToolKind.Ellipse => StrokeKind.Ellipse,
            _ => StrokeKind.Freehand
        };

        PushUndoSnapshot();
        _pendingUndo = true;
        ActiveStroke = new Stroke(kind, color, width)
        {
            IsHighlighter = tool == ToolKind.Highlighter
        };
        ActiveStroke.AddPoint(start.X, start.Y, start.Pressure);
        LiveChanged?.Invoke(this, EventArgs.Empty);
        return ActiveStroke;
    }

    public void UpdateActivePoint(StrokePoint point, double minimumDistance = 0.8)
    {
        var active = ActiveStroke;
        if (active is null)
        {
            return;
        }

        if (active.Kind == StrokeKind.Freehand)
        {
            if (active.Points.Count == 0)
            {
                active.AddPoint(point.X, point.Y, point.Pressure);
            }
            else
            {
                var last = active.Points[^1];
                var dx = point.X - last.X;
                var dy = point.Y - last.Y;
                if ((dx * dx + dy * dy) >= minimumDistance * minimumDistance)
                {
                    active.AddPoint(point.X, point.Y, point.Pressure);
                }
            }
        }
        else if (active.Kind == StrokeKind.Text)
        {
            // Text is created in one shot.
        }
        else
        {
            if (active.Points.Count == 0)
            {
                active.AddPoint(point.X, point.Y, point.Pressure);
            }
            else if (active.Points.Count == 1)
            {
                active.AddPoint(point.X, point.Y, point.Pressure);
            }
            else
            {
                active.Points[1] = new StrokePoint(point.X, point.Y, point.Pressure);
            }
        }

        LiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public void EndStroke()
    {
        var active = ActiveStroke;
        if (active is null)
        {
            return;
        }

        ActiveStroke = null;

        if (IsDegenerate(active))
        {
            if (_pendingUndo)
            {
                if (_undo.Count > 0)
                {
                    _undo.Pop();
                }

                _pendingUndo = false;
            }

            LiveChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _strokes.Add(active);
        _pendingUndo = false;
        _redo.Clear();
        LiveChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void CancelActiveStroke(bool silent = false)
    {
        if (ActiveStroke is null)
        {
            return;
        }

        ActiveStroke = null;
        if (_pendingUndo)
        {
            if (_undo.Count > 0)
            {
                _undo.Pop();
            }

            _pendingUndo = false;
        }

        if (!silent)
        {
            LiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void AddText(Point point, string text, Color color, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        PushUndoSnapshot();
        var stroke = new Stroke(StrokeKind.Text, color, 1)
        {
            Text = text.TrimEnd(),
            FontSize = fontSize
        };
        stroke.AddPoint(point.X, point.Y, 1.0);
        _strokes.Add(stroke);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void BeginErase()
    {
        _eraseUndoPushed = false;
    }

    public void EraseSegment(Point a, Point b, double radius)
    {
        if (_strokes.Count == 0)
        {
            return;
        }

        var next = new List<Stroke>(_strokes.Count);
        var changed = false;
        var hitRadius = Math.Max(1.0, radius);

        foreach (var stroke in _strokes)
        {
            if (stroke.Kind == StrokeKind.Freehand)
            {
                var pieces = SplitFreehand(stroke, a, b, hitRadius);
                if (pieces is null)
                {
                    next.Add(stroke);
                }
                else
                {
                    changed = true;
                    next.AddRange(pieces);
                }
            }
            else
            {
                var distance = DistanceToObject(stroke, a, b);
                if (distance <= hitRadius + stroke.Width * 0.5)
                {
                    changed = true;
                }
                else
                {
                    next.Add(stroke);
                }
            }
        }

        if (!changed)
        {
            return;
        }

        if (!_eraseUndoPushed)
        {
            PushUndoSnapshot(keepActive: false);
            _eraseUndoPushed = true;
        }

        _strokes.Clear();
        _strokes.AddRange(next);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        CancelActiveStroke(silent: true);
        _redo.Push(new List<Stroke>(_strokes));
        var state = _undo.Pop();
        _strokes.Clear();
        _strokes.AddRange(state);
        _pendingUndo = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        CancelActiveStroke(silent: true);
        _undo.Push(new List<Stroke>(_strokes));
        var state = _redo.Pop();
        _strokes.Clear();
        _strokes.AddRange(state);
        _pendingUndo = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_strokes.Count == 0 && ActiveStroke is null)
        {
            return;
        }

        CancelActiveStroke(silent: true);
        PushUndoSnapshot();
        _strokes.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void TickFade(DateTime nowUtc)
    {
        var lifetime = FadeLifetime;
        if (lifetime is null || lifetime <= TimeSpan.Zero || _strokes.Count == 0)
        {
            return;
        }

        var removed = false;
        for (var i = _strokes.Count - 1; i >= 0; i--)
        {
            if (nowUtc - _strokes[i].CreatedUtc >= lifetime.Value)
            {
                _strokes.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            FadeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PushUndoSnapshot(bool keepActive = true)
    {
        _undo.Push(new List<Stroke>(_strokes));
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Take(MaxUndo).Reverse().ToArray();
            _undo.Clear();
            foreach (var item in keep)
            {
                _undo.Push(item);
            }
        }

        _redo.Clear();
        _pendingUndo = keepActive;
    }

    private static bool IsDegenerate(Stroke stroke)
    {
        if (stroke.Kind == StrokeKind.Freehand)
        {
            return stroke.Points.Count == 0;
        }

        if (stroke.Points.Count < 2)
        {
            return true;
        }

        var a = stroke.Points[0];
        var b = stroke.Points[1];
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (dx * dx + dy * dy) < 9.0;
    }

    private static List<Stroke>? SplitFreehand(Stroke stroke, Point a, Point b, double radius)
    {
        var count = stroke.Points.Count;
        if (count == 0)
        {
            return null;
        }

        var hit = new bool[count];
        var any = false;
        for (var i = 0; i < count; i++)
        {
            var p = stroke.Points[i].ToPoint();
            if (DistancePointToSegment(p, a, b) <= radius)
            {
                hit[i] = true;
                any = true;
            }

            if (i + 1 < count)
            {
                var mid = new Point(
                    (stroke.Points[i].X + stroke.Points[i + 1].X) * 0.5,
                    (stroke.Points[i].Y + stroke.Points[i + 1].Y) * 0.5);
                if (DistancePointToSegment(mid, a, b) <= radius)
                {
                    hit[i] = true;
                    hit[i + 1] = true;
                    any = true;
                }
            }
        }

        if (!any)
        {
            return null;
        }

        var result = new List<Stroke>();
        var run = new List<StrokePoint>();
        void Flush()
        {
            if (run.Count == 0)
            {
                return;
            }

            if (run.Count >= 2 || (count == 1 && run.Count == 1))
            {
                var piece = new Stroke(stroke.Kind, stroke.Color, stroke.Width)
                {
                    IsHighlighter = stroke.IsHighlighter,
                    CreatedUtc = stroke.CreatedUtc
                };
                piece.Points.AddRange(run);
                result.Add(piece);
            }

            run = new List<StrokePoint>();
        }

        for (var i = 0; i < count; i++)
        {
            if (hit[i])
            {
                Flush();
            }
            else
            {
                run.Add(stroke.Points[i]);
            }
        }

        Flush();
        return result;
    }

    private static double DistanceToObject(Stroke stroke, Point a, Point b)
    {
        if (stroke.Kind == StrokeKind.Text)
        {
            var bounds = stroke.GetBounds();
            var expanded = bounds;
            expanded.Inflate(stroke.FontSize * 0.15, stroke.FontSize * 0.15);
            if (expanded.Contains(a) || expanded.Contains(b))
            {
                return 0;
            }

            var d0 = DistancePointToSegment(new Point(expanded.Left, expanded.Top), a, b);
            var d1 = DistancePointToSegment(new Point(expanded.Right, expanded.Top), a, b);
            var d2 = DistancePointToSegment(new Point(expanded.Right, expanded.Bottom), a, b);
            var d3 = DistancePointToSegment(new Point(expanded.Left, expanded.Bottom), a, b);
            return Math.Min(Math.Min(d0, d1), Math.Min(d2, d3));
        }

        if (stroke.Points.Count < 2)
        {
            return stroke.Points.Count == 1
                ? DistancePointToSegment(stroke.Points[0].ToPoint(), a, b)
                : double.MaxValue;
        }

        var p0 = stroke.Points[0].ToPoint();
        var p1 = stroke.Points[1].ToPoint();

        return stroke.Kind switch
        {
            StrokeKind.Line or StrokeKind.Arrow => DistanceSegmentToSegment(p0, p1, a, b),
            StrokeKind.Rectangle => DistanceToRect(stroke, a, b),
            StrokeKind.Ellipse => DistanceToEllipse(stroke, a, b),
            _ => DistanceSegmentToSegment(p0, p1, a, b)
        };
    }

    private static double DistanceToRect(Stroke stroke, Point a, Point b)
    {
        var x0 = Math.Min(stroke.Points[0].X, stroke.Points[1].X);
        var y0 = Math.Min(stroke.Points[0].Y, stroke.Points[1].Y);
        var x1 = Math.Max(stroke.Points[0].X, stroke.Points[1].X);
        var y1 = Math.Max(stroke.Points[0].Y, stroke.Points[1].Y);
        return Math.Min(
            Math.Min(DistanceSegmentToSegment(new Point(x0, y0), new Point(x1, y0), a, b),
                     DistanceSegmentToSegment(new Point(x1, y0), new Point(x1, y1), a, b)),
            Math.Min(DistanceSegmentToSegment(new Point(x1, y1), new Point(x0, y1), a, b),
                     DistanceSegmentToSegment(new Point(x0, y1), new Point(x0, y0), a, b)));
    }

    private static double DistanceToEllipse(Stroke stroke, Point a, Point b)
    {
        var cx = (stroke.Points[0].X + stroke.Points[1].X) * 0.5;
        var cy = (stroke.Points[0].Y + stroke.Points[1].Y) * 0.5;
        var rx = Math.Abs(stroke.Points[0].X - stroke.Points[1].X) * 0.5;
        var ry = Math.Abs(stroke.Points[0].Y - stroke.Points[1].Y) * 0.5;
        if (rx < 0.5 || ry < 0.5)
        {
            return double.MaxValue;
        }

        var previous = new Point(cx + rx, cy);
        var best = double.MaxValue;
        const int steps = 64;
        for (var i = 1; i <= steps; i++)
        {
            var angle = Math.PI * 2 * i / steps;
            var point = new Point(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle));
            best = Math.Min(best, DistanceSegmentToSegment(previous, point, a, b));
            previous = point;
        }

        return best;
    }

    private static double DistancePointToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        if (Math.Abs(dx) < 0.0001 && Math.Abs(dy) < 0.0001)
        {
            return (p - a).Length;
        }

        var t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0.0, 1.0);
        var projection = new Point(a.X + t * dx, a.Y + t * dy);
        return (p - projection).Length;
    }

    private static double DistanceSegmentToSegment(Point p1, Point p2, Point p3, Point p4)
    {
        return Math.Min(
            Math.Min(DistancePointToSegment(p1, p3, p4), DistancePointToSegment(p2, p3, p4)),
            Math.Min(DistancePointToSegment(p3, p1, p2), DistancePointToSegment(p4, p1, p2)));
    }
}
