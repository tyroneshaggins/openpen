namespace OpenPen.Models;

public enum ToolKind
{
    PassThrough,
    Pen,
    Highlighter,
    Eraser,
    Text,
    Line,
    Arrow,
    Rectangle,
    Ellipse
}

public enum WhiteboardMode
{
    None,
    White,
    Black
}

public enum FadingMode
{
    Off,
    Slow,
    Medium,
    Fast
}

public static class ToolKindExtensions
{
    public static bool IsDrawingTool(this ToolKind tool) => tool != ToolKind.PassThrough;

    public static bool IsShape(this ToolKind tool) =>
        tool is ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse;

    public static string DisplayName(this ToolKind tool) => tool switch
    {
        ToolKind.PassThrough => "Pass-through",
        ToolKind.Pen => "Pen",
        ToolKind.Highlighter => "Highlighter",
        ToolKind.Eraser => "Eraser",
        ToolKind.Text => "Text",
        ToolKind.Line => "Line",
        ToolKind.Arrow => "Arrow",
        ToolKind.Rectangle => "Rectangle",
        ToolKind.Ellipse => "Ellipse",
        _ => tool.ToString()
    };

    public static string HotkeyHint(this ToolKind tool) => tool switch
    {
        ToolKind.PassThrough => "Ctrl+Shift+8",
        ToolKind.Pen => "Ctrl+Shift+3",
        ToolKind.Highlighter => "Ctrl+Shift+4",
        ToolKind.Eraser => "Ctrl+Shift+5",
        ToolKind.Text => "Ctrl+Shift+T",
        ToolKind.Line => "Ctrl+Shift+L",
        ToolKind.Arrow => "Ctrl+Shift+A",
        ToolKind.Rectangle => "Ctrl+Shift+R",
        ToolKind.Ellipse => "Ctrl+Shift+E",
        _ => string.Empty
    };
}

public static class FadingModeExtensions
{
    public static TimeSpan? Lifetime(this FadingMode mode) => mode switch
    {
        FadingMode.Slow => TimeSpan.FromSeconds(20),
        FadingMode.Medium => TimeSpan.FromSeconds(10),
        FadingMode.Fast => TimeSpan.FromSeconds(5),
        _ => null
    };

    public static string DisplayName(this FadingMode mode) => mode switch
    {
        FadingMode.Slow => "Slow · 20s",
        FadingMode.Medium => "Medium · 10s",
        FadingMode.Fast => "Fast · 5s",
        _ => "Off"
    };
}
