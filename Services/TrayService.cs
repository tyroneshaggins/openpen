using Drawing = System.Drawing;
using System.Windows.Forms;
using OpenPen.Models;

namespace OpenPen.Services;

public sealed class TrayService : IDisposable
{
    private readonly AppController _controller;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _drawItem;
    private readonly ToolStripMenuItem _penItem;
    private readonly ToolStripMenuItem _highlighterItem;
    private readonly ToolStripMenuItem _eraserItem;
    private readonly ToolStripMenuItem _textItem;
    private readonly ToolStripMenuItem _whiteboardItem;
    private readonly ToolStripMenuItem _blackboardItem;
    private readonly ToolStripMenuItem _spotlightItem;
    private readonly ToolStripMenuItem _fadeItem;
    private readonly ToolStripMenuItem _toolbarItem;
    private readonly ToolStripMenuItem _clearItem;
    private readonly ToolStripMenuItem _startupItem;
    private bool _disposed;

    public TrayService(AppController controller)
    {
        _controller = controller;

        _menu = new ContextMenuStrip
        {
            BackColor = Drawing.Color.FromArgb(31, 34, 41),
            ForeColor = Drawing.Color.FromArgb(244, 246, 250),
            Font = new Drawing.Font("Segoe UI", 9F, Drawing.FontStyle.Regular),
            ShowImageMargin = true,
            ShowCheckMargin = true,
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()) { RoundedEdges = false },
            Padding = new Padding(4, 6, 4, 6),
            DropShadowEnabled = true
        };

        var header = new ToolStripMenuItem("OpenPen")
        {
            Enabled = false,
            Font = new Drawing.Font("Segoe UI Semibold", 9.5F, Drawing.FontStyle.Bold),
            ForeColor = Drawing.Color.FromArgb(255, 122, 69)
        };
        _menu.Items.Add(header);
        _menu.Items.Add(new ToolStripSeparator());

        _drawItem = Add("Draw on screen", (_, _) => _controller.ToggleDrawingMode());
        _menu.Items.Add(new ToolStripSeparator());
        _penItem = Add("Pen", (_, _) => _controller.SetTool(ToolKind.Pen));
        _highlighterItem = Add("Highlighter", (_, _) => _controller.SetTool(ToolKind.Highlighter));
        _eraserItem = Add("Eraser", (_, _) => _controller.SetTool(ToolKind.Eraser));
        _textItem = Add("Text", (_, _) => _controller.SetTool(ToolKind.Text));
        _menu.Items.Add(new ToolStripSeparator());
        _whiteboardItem = Add("Whiteboard", (_, _) => _controller.SetWhiteboard(WhiteboardMode.White));
        _blackboardItem = Add("Blackboard", (_, _) => _controller.SetWhiteboard(WhiteboardMode.Black));
        _spotlightItem = Add("Spotlight cursor", (_, _) => _controller.ToggleSpotlight());
        _fadeItem = Add("Fading ink", (_, _) => _controller.ToggleFading());
        _menu.Items.Add(new ToolStripSeparator());
        _toolbarItem = Add("Show toolbar", (_, _) => _controller.ShowToolbar());
        Add("Screenshot", (_, _) => _ = _controller.TakeScreenshotAsync());
        _clearItem = Add("Clear ink", (_, _) => _controller.ClearInk());
        Add("Settings & shortcuts...", (_, _) => _controller.OpenSettings());
        _menu.Items.Add(new ToolStripSeparator());
        _startupItem = Add("Start with Windows", (_, _) => _controller.SetStartWithWindows(!_controller.State.StartWithWindows));
        _menu.Items.Add(new ToolStripSeparator());
        Add("Exit OpenPen", (_, _) => _controller.Exit());

        _menu.Opening += (_, _) => Refresh();

        _icon = new NotifyIcon
        {
            Text = "OpenPen",
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.DoubleClick += (_, _) =>
        {
            if (!_controller.State.ToolbarVisible)
            {
                _controller.ShowToolbar();
            }

            _controller.ToggleDrawingMode();
        };

        Refresh();
    }

    public void Initialize()
    {
        Refresh();
    }

    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var state = _controller.State;
        _drawItem.Checked = state.ActiveTool != ToolKind.PassThrough;
        _penItem.Checked = state.ActiveTool == ToolKind.Pen;
        _highlighterItem.Checked = state.ActiveTool == ToolKind.Highlighter;
        _eraserItem.Checked = state.ActiveTool == ToolKind.Eraser;
        _textItem.Checked = state.ActiveTool == ToolKind.Text;
        _whiteboardItem.Checked = state.Whiteboard == WhiteboardMode.White;
        _blackboardItem.Checked = state.Whiteboard == WhiteboardMode.Black;
        _spotlightItem.Checked = state.SpotlightEnabled;
        _fadeItem.Checked = state.Fading != FadingMode.Off;
        _toolbarItem.Checked = state.ToolbarVisible;
        _clearItem.Enabled = _controller.Document.HasStrokes;
        _startupItem.Checked = state.StartWithWindows;

        var mode = state.ActiveTool switch
        {
            ToolKind.PassThrough => "pass-through",
            _ => state.ActiveTool.DisplayName().ToLowerInvariant()
        };
        var text = $"OpenPen — {mode}";
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    private ToolStripMenuItem Add(string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Padding = new Padding(0, 3, 12, 3)
        };
        item.Click += onClick;
        _menu.Items.Add(item);
        return item;
    }

    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(exe);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // fall through
        }

        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Drawing.Color Background = Drawing.Color.FromArgb(31, 34, 41);
        private static readonly Drawing.Color Hover = Drawing.Color.FromArgb(48, 52, 61);
        private static readonly Drawing.Color Border = Drawing.Color.FromArgb(58, 62, 72);

        public override Drawing.Color ToolStripDropDownBackground => Background;
        public override Drawing.Color ImageMarginGradientBegin => Background;
        public override Drawing.Color ImageMarginGradientMiddle => Background;
        public override Drawing.Color ImageMarginGradientEnd => Background;
        public override Drawing.Color MenuBorder => Border;
        public override Drawing.Color MenuItemBorder => Hover;
        public override Drawing.Color MenuItemSelected => Hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override Drawing.Color MenuItemPressedGradientBegin => Hover;
        public override Drawing.Color MenuItemPressedGradientEnd => Hover;
        public override Drawing.Color CheckBackground => Drawing.Color.FromArgb(52, 255, 122, 69);
        public override Drawing.Color CheckSelectedBackground => Drawing.Color.FromArgb(72, 255, 122, 69);
        public override Drawing.Color CheckPressedBackground => Drawing.Color.FromArgb(72, 255, 122, 69);
        public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(45, 48, 56);
        public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(45, 48, 56);
    }
}
