using System.Windows.Media;
using OpenPen.Models;

namespace OpenPen.Services;

public sealed class AppState
{
    public ToolKind ActiveTool { get; set; } = ToolKind.PassThrough;
    public ToolKind LastDrawingTool { get; set; } = ToolKind.Pen;
    public Color InkColor { get; set; } = ColorUtil.DefaultInk;
    public Color LastColor { get; set; } = Colors.White;
    public double PenSize { get; set; } = 4;
    public double HighlighterSize { get; set; } = 20;
    public double EraserSize { get; set; } = 28;
    public double ShapeSize { get; set; } = 4;
    public double TextSize { get; set; } = 28;
    public double InkOpacity { get; set; } = 1.0;
    public double SpotlightRadius { get; set; } = 170;
    public bool InkVisible { get; set; } = true;
    public WhiteboardMode Whiteboard { get; set; } = WhiteboardMode.None;
    public bool SpotlightEnabled { get; set; }
    public FadingMode Fading { get; set; } = FadingMode.Off;
    public bool ToolbarVisible { get; set; } = true;
    public bool HotkeysEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }

    public bool OverlayShouldBeVisible => InkVisible || Whiteboard != WhiteboardMode.None || SpotlightEnabled;

    public bool OverlayIsInteractive =>
        Whiteboard != WhiteboardMode.None ||
        (InkVisible && !SpotlightEnabled && ActiveTool.IsDrawingTool());

    public bool ShouldRenderInk => InkVisible;

    public double CurrentSize
    {
        get => ActiveTool switch
        {
            ToolKind.Highlighter => HighlighterSize,
            ToolKind.Eraser => EraserSize,
            ToolKind.Text => TextSize,
            ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse => ShapeSize,
            ToolKind.PassThrough => SizeForTool(LastDrawingTool),
            _ => PenSize
        };
        set
        {
            var clamped = Math.Clamp(value, 1, 120);
            switch (ActiveTool)
            {
                case ToolKind.Highlighter:
                    HighlighterSize = clamped;
                    break;
                case ToolKind.Eraser:
                    EraserSize = clamped;
                    break;
                case ToolKind.Text:
                    TextSize = clamped;
                    break;
                case ToolKind.Line:
                case ToolKind.Arrow:
                case ToolKind.Rectangle:
                case ToolKind.Ellipse:
                    ShapeSize = clamped;
                    break;
                case ToolKind.PassThrough:
                    SetSizeForTool(LastDrawingTool, clamped);
                    break;
                default:
                    PenSize = clamped;
                    break;
            }
        }
    }

    public double SizeForTool(ToolKind tool) => tool switch
    {
        ToolKind.Highlighter => HighlighterSize,
        ToolKind.Eraser => EraserSize,
        ToolKind.Text => TextSize,
        ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse => ShapeSize,
        _ => PenSize
    };

    public void SetSizeForTool(ToolKind tool, double value)
    {
        var clamped = Math.Clamp(value, 1, 120);
        switch (tool)
        {
            case ToolKind.Highlighter:
                HighlighterSize = clamped;
                break;
            case ToolKind.Eraser:
                EraserSize = clamped;
                break;
            case ToolKind.Text:
                TextSize = clamped;
                break;
            case ToolKind.Line:
            case ToolKind.Arrow:
            case ToolKind.Rectangle:
            case ToolKind.Ellipse:
                ShapeSize = clamped;
                break;
            default:
                PenSize = clamped;
                break;
        }
    }

    public void AdjustCurrentSize(int direction)
    {
        var current = CurrentSize;
        var step = ActiveTool switch
        {
            ToolKind.Text => current < 24 ? 2 : 4,
            _ => current < 3 ? 0.5 : current < 10 ? 1 : current < 24 ? 2 : 4
        };
        CurrentSize = current + step * Math.Sign(direction);
    }
}
