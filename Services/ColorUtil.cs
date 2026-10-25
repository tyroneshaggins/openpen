using System.Globalization;
using System.Windows.Media;

namespace OpenPen.Services;

public static class ColorUtil
{
    public sealed record NamedColor(string Name, Color Color, string Hex);

    public static readonly IReadOnlyList<NamedColor> Palette = new NamedColor[]
    {
        new("Sun", Color.FromRgb(0xFF, 0xD4, 0x00), "#FFFFD400"),
        new("Amber", Color.FromRgb(0xFF, 0xB3, 0x00), "#FFFFB300"),
        new("Orange", Color.FromRgb(0xFF, 0x7A, 0x45), "#FFFF7A45"),
        new("Coral", Color.FromRgb(0xFF, 0x5A, 0x5A), "#FFFF5A5A"),
        new("Pink", Color.FromRgb(0xFF, 0x40, 0x81), "#FFFF4081"),
        new("Magenta", Color.FromRgb(0xE0, 0x40, 0xFB), "#FFE040FB"),
        new("Purple", Color.FromRgb(0x9C, 0x6B, 0xFF), "#FF9C6BFF"),
        new("Indigo", Color.FromRgb(0x53, 0x6D, 0xFE), "#FF536DFE"),
        new("Blue", Color.FromRgb(0x44, 0x8A, 0xFF), "#FF448AFF"),
        new("Sky", Color.FromRgb(0x40, 0xC4, 0xFF), "#FF40C4FF"),
        new("Cyan", Color.FromRgb(0x18, 0xFF, 0xFF), "#FF18FFFF"),
        new("Teal", Color.FromRgb(0x1D, 0xE9, 0xB6), "#FF1DE9B6"),
        new("Green", Color.FromRgb(0x2E, 0xD5, 0x73), "#FF2ED573"),
        new("Lime", Color.FromRgb(0xC6, 0xFF, 0x00), "#FFC6FF00"),
        new("Red", Color.FromRgb(0xFF, 0x2D, 0x55), "#FFFF2D55"),
        new("Rose", Color.FromRgb(0xFF, 0x6B, 0x9D), "#FFFF6B9D"),
        new("White", Colors.White, "#FFFFFFFF"),
        new("Silver", Color.FromRgb(0xC9, 0xD1, 0xDD), "#FFC9D1DD"),
        new("Gray", Color.FromRgb(0x8A, 0x93, 0xA3), "#FF8A93A3"),
        new("Slate", Color.FromRgb(0x4A, 0x55, 0x68), "#FF4A5568"),
        new("Black", Colors.Black, "#FF000000"),
        new("Brown", Color.FromRgb(0xA1, 0x6B, 0x4C), "#FFA16B4C"),
        new("Navy", Color.FromRgb(0x1F, 0x3A, 0x93), "#FF1F3A93"),
        new("Forest", Color.FromRgb(0x20, 0x7A, 0x4B), "#FF207A4B")
    };

    public static readonly Color DefaultInk = Color.FromRgb(0xFF, 0xD4, 0x00);

    public static string ToHex(Color color) =>
        color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static bool TryParse(string? text, out Color color)
    {
        color = Colors.White;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        if (value.Length is not (7 or 9))
        {
            return false;
        }

        if (!uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw))
        {
            return false;
        }

        if (value.Length == 7)
        {
            color = Color.FromRgb((byte)((raw >> 16) & 0xFF), (byte)((raw >> 8) & 0xFF), (byte)(raw & 0xFF));
            return true;
        }

        color = Color.FromArgb((byte)((raw >> 24) & 0xFF), (byte)((raw >> 16) & 0xFF),
            (byte)((raw >> 8) & 0xFF), (byte)(raw & 0xFF));
        return true;
    }

    public static Color WithAlpha(Color color, double alpha)
    {
        var a = (byte)Math.Clamp((int)Math.Round(color.A * alpha), 0, 255);
        return Color.FromArgb(a, color.R, color.G, color.B);
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    public static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
}
