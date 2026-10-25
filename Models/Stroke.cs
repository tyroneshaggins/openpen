using System.Windows;
using System.Windows.Media;

namespace OpenPen.Models;

public enum StrokeKind
{
    Freehand,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Text
}

public readonly record struct StrokePoint(double X, double Y, double Pressure)
{
    public Point ToPoint() => new(X, Y);
}

public sealed class Stroke
{
    public StrokeKind Kind { get; init; }
    public List<StrokePoint> Points { get; } = new();
    public Color Color { get; init; }
    public double Width { get; init; }
    public bool IsHighlighter { get; init; }
    public string Text { get; init; } = string.Empty;
    public double FontSize { get; init; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    public Stroke(StrokeKind kind, Color color, double width)
    {
        Kind = kind;
        Color = color;
        Width = width;
    }

    public Stroke AddPoint(double x, double y, double pressure = 1.0)
    {
        Points.Add(new StrokePoint(x, y, Math.Clamp(pressure, 0.05, 1.0)));
        return this;
    }

    public Rect GetBounds()
    {
        if (Points.Count == 0)
        {
            return Rect.Empty;
        }

        if (Kind == StrokeKind.Text)
        {
            var p = Points[0].ToPoint();
            var lines = Text.Replace("\r", string.Empty).Split('\n');
            var longest = 1;
            foreach (var line in lines)
            {
                longest = Math.Max(longest, line.Length);
            }

            var width = Math.Max(FontSize * 0.7, longest * FontSize * 0.62);
            var height = Math.Max(FontSize, lines.Length * FontSize * 1.32);
            return new Rect(p.X, p.Y, width, height);
        }

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var point in Points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        var pad = Math.Max(Width, 2) * (Kind == StrokeKind.Freehand ? 1.3 : 1.8) + 6;
        return new Rect(new Point(minX - pad, minY - pad), new Point(maxX + pad, maxY + pad));
    }

    public double GetFadeAlpha(DateTime nowUtc, TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            return 1.0;
        }

        var age = nowUtc - CreatedUtc;
        if (age <= TimeSpan.Zero)
        {
            return 1.0;
        }

        var hold = TimeSpan.FromTicks((long)(lifetime.Ticks * 0.3));
        if (age <= hold)
        {
            return 1.0;
        }

        var fadeSpan = lifetime - hold;
        if (fadeSpan <= TimeSpan.Zero)
        {
            return 0.0;
        }

        var t = (age - hold).TotalMilliseconds / fadeSpan.TotalMilliseconds;
        return Math.Clamp(1.0 - t, 0.0, 1.0);
    }
}
