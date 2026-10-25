using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OpenPen.Models;
using OpenPen.Services;

namespace OpenPen.Rendering;

public static class InkRenderer
{
    public static void DrawStroke(
        DrawingContext dc,
        Stroke stroke,
        Func<Point, Point> toLocal,
        double fadeAlpha,
        double pixelsPerDip)
    {
        if (stroke.Points.Count == 0 || fadeAlpha <= 0.004)
        {
            return;
        }

        var colorAlpha = stroke.Color.A / 255.0;
        var alpha = Math.Clamp(fadeAlpha * colorAlpha, 0, 1);
        if (alpha <= 0.004)
        {
            return;
        }

        var points = new Point[stroke.Points.Count];
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            points[i] = toLocal(stroke.Points[i].ToPoint());
        }

        switch (stroke.Kind)
        {
            case StrokeKind.Freehand:
                DrawFreehand(dc, stroke, points, alpha);
                break;
            case StrokeKind.Line:
                DrawLine(dc, stroke, points, alpha);
                break;
            case StrokeKind.Arrow:
                DrawArrow(dc, stroke, points, alpha);
                break;
            case StrokeKind.Rectangle:
                DrawRectangle(dc, stroke, points, alpha);
                break;
            case StrokeKind.Ellipse:
                DrawEllipse(dc, stroke, points, alpha);
                break;
            case StrokeKind.Text:
                DrawText(dc, stroke, points[0], alpha, pixelsPerDip);
                break;
        }
    }

    private static void DrawFreehand(DrawingContext dc, Stroke stroke, Point[] points, double alpha)
    {
        var width = Math.Max(0.6, stroke.Width);
        if (points.Length == 1)
        {
            var brush = FillBrush(stroke.Color, stroke.IsHighlighter ? alpha * 0.5 : alpha);
            var radius = Math.Max(0.8, width * 0.5);
            dc.DrawEllipse(brush, null, points[0], radius, radius);
            return;
        }

        if (stroke.IsHighlighter)
        {
            var brush = FillBrush(stroke.Color, stroke.IsHighlighter ? alpha * 0.44 : alpha);
            var pen = new Pen(brush, width)
            {
                StartLineCap = PenLineCap.Square,
                EndLineCap = PenLineCap.Square,
                LineJoin = PenLineJoin.Round
            };

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], isFilled: false, isClosed: false);
                ctx.PolyLineTo(points.AsSpan(1).ToArray(), isStroked: true, isSmoothJoin: true);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
            return;
        }

        if (width < 1.4)
        {
            var brush = FillBrush(stroke.Color, alpha);
            var pen = new Pen(brush, width)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], isFilled: false, isClosed: false);
                ctx.PolyLineTo(points.AsSpan(1).ToArray(), isStroked: true, isSmoothJoin: true);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
            return;
        }

        // Build a variable-width ribbon so pen pressure and fast flicks both feel natural.
        var count = points.Length;
        var left = new Point[count];
        var right = new Point[count];
        var halfWidths = new double[count];
        var lastTangent = new Vector(1, 0);

        for (var i = 0; i < count; i++)
        {
            var previous = i > 0 ? points[i - 1] : points[i];
            var next = i < count - 1 ? points[i + 1] : points[i];
            var tangent = next - previous;

            if (tangent.LengthSquared < 0.000001)
            {
                tangent = i > 0 ? points[i] - points[i - 1] : new Vector(1, 0);
            }

            if (tangent.LengthSquared < 0.000001)
            {
                tangent = lastTangent;
            }
            else
            {
                tangent.Normalize();
                lastTangent = tangent;
            }

            var normal = new Vector(-tangent.Y, tangent.X);
            var pressure = Math.Clamp(stroke.Points[i].Pressure, 0.05, 1.0);
            var pressureScale = 0.36 + pressure * 0.64;
            var half = Math.Max(0.55, width * pressureScale * 0.5);
            halfWidths[i] = half;
            left[i] = points[i] + normal * half;
            right[i] = points[i] - normal * half;
        }

        var ribbon = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = ribbon.Open())
        {
            ctx.BeginFigure(left[0], isFilled: true, isClosed: true);
            for (var i = 1; i < count; i++)
            {
                ctx.LineTo(left[i], isStroked: true, isSmoothJoin: true);
            }

            for (var i = count - 1; i >= 0; i--)
            {
                ctx.LineTo(right[i], isStroked: true, isSmoothJoin: true);
            }
        }

        ribbon.Freeze();
        var solid = FillBrush(stroke.Color, alpha);
        dc.DrawGeometry(solid, null, ribbon);
        dc.DrawEllipse(solid, null, points[0], halfWidths[0], halfWidths[0]);
        dc.DrawEllipse(solid, null, points[count - 1], halfWidths[count - 1], halfWidths[count - 1]);
    }

    private static void DrawLine(DrawingContext dc, Stroke stroke, Point[] points, double alpha)
    {
        if (points.Length < 2)
        {
            return;
        }

        var pen = StrokePen(stroke, alpha);
        dc.DrawLine(pen, points[0], points[1]);
    }

    private static void DrawArrow(DrawingContext dc, Stroke stroke, Point[] points, double alpha)
    {
        if (points.Length < 2)
        {
            return;
        }

        var start = points[0];
        var end = points[1];
        var direction = end - start;
        var length = direction.Length;
        if (length < 1.0)
        {
            return;
        }

        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        var headLength = Math.Min(length * 0.52, Math.Max(11.0, stroke.Width * 3.6));
        var headHalf = headLength * 0.52;
        var basePoint = end - direction * headLength;

        var pen = StrokePen(stroke, alpha);
        dc.DrawLine(pen, start, end - direction * Math.Min(headLength * 0.55, length * 0.4));

        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(end, isFilled: true, isClosed: true);
            ctx.LineTo(basePoint + normal * headHalf, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(basePoint - normal * headHalf, isStroked: true, isSmoothJoin: false);
        }

        head.Freeze();
        dc.DrawGeometry(FillBrush(stroke.Color, alpha), null, head);
    }

    private static void DrawRectangle(DrawingContext dc, Stroke stroke, Point[] points, double alpha)
    {
        if (points.Length < 2)
        {
            return;
        }

        var p0 = points[0];
        var p1 = points[1];
        var rect = new Rect(new Point(Math.Min(p0.X, p1.X), Math.Min(p0.Y, p1.Y)),
            new Point(Math.Max(p0.X, p1.X), Math.Max(p0.Y, p1.Y)));
        if (rect.Width < 1 || rect.Height < 1)
        {
            return;
        }

        var pen = StrokePen(stroke, alpha);
        var radius = Math.Min(6, Math.Min(rect.Width, rect.Height) * 0.08);
        dc.DrawRoundedRectangle(null, pen, rect, radius, radius);
    }

    private static void DrawEllipse(DrawingContext dc, Stroke stroke, Point[] points, double alpha)
    {
        if (points.Length < 2)
        {
            return;
        }

        var p0 = points[0];
        var p1 = points[1];
        var center = new Point((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5);
        var rx = Math.Abs(p1.X - p0.X) * 0.5;
        var ry = Math.Abs(p1.Y - p0.Y) * 0.5;
        if (rx < 0.5 || ry < 0.5)
        {
            return;
        }

        dc.DrawEllipse(null, StrokePen(stroke, alpha), center, rx, ry);
    }

    private static void DrawText(DrawingContext dc, Stroke stroke, Point origin, double alpha, double pixelsPerDip)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var brush = FillBrush(stroke.Color, alpha);
        var text = new FormattedText(
            stroke.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(8, stroke.FontSize),
            brush,
            pixelsPerDip)
        {
            TextAlignment = TextAlignment.Left
        };

        var shadow = new FormattedText(
            stroke.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(8, stroke.FontSize),
            new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(110 * alpha, 0, 110), 0, 0, 0)),
            pixelsPerDip);

        dc.DrawText(shadow, new Point(origin.X + 1.4, origin.Y + 1.4));
        dc.DrawText(text, origin);
    }

    private static Pen StrokePen(Stroke stroke, double alpha)
    {
        return new Pen(FillBrush(stroke.Color, alpha), Math.Max(0.6, stroke.Width))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
    }

    private static SolidColorBrush FillBrush(Color color, double alpha)
    {
        var brush = new SolidColorBrush(ColorUtil.WithAlpha(color, alpha));
        brush.Freeze();
        return brush;
    }
}
