using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OpenPen.Interop;
using OpenPen.Models;
using OpenPen.Services;

namespace OpenPen.Views;

public partial class ToolbarWindow : Window
{
    private readonly AppController _controller;
    private readonly Dictionary<ToolKind, ToggleButton> _toolButtons = new();
    private readonly List<(Color Color, ToggleButton Button)> _swatches = new();
    private bool _dragging;
    private Point _dragOriginCursor;
    private Rect _dragOriginWindow;
    private bool _positioned;
    private bool _allowClose;
    private bool _expanded;
    private bool _updatingOpacity;
    private HwndSource? _source;

    public ToolbarWindow(AppController controller)
    {
        _controller = controller;
        _updatingOpacity = true;
        InitializeComponent();
        _updatingOpacity = false;

        BuildToolMap();
        BuildSwatches();
        ApplyExpandedState(expanded: false, keepBottom: false);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        _controller.StateChanged += OnStateChanged;

        Show();
        Refresh();
    }

    public void Refresh()
    {
        var state = _controller.State;
        var document = _controller.Document;

        foreach (var pair in _toolButtons)
        {
            pair.Value.IsChecked = state.ActiveTool == pair.Key;
        }

        UndoButton.IsEnabled = document.CanUndo;
        RedoButton.IsEnabled = document.CanRedo;
        ClearButton.IsEnabled = document.HasStrokes;
        WhiteboardButton.IsChecked = state.Whiteboard != WhiteboardMode.None;
        SpotlightButton.IsChecked = state.SpotlightEnabled;
        FadeButton.IsChecked = state.Fading != FadingMode.Off;
        InkVisibleButton.IsChecked = state.InkVisible;
        SizeText.Text = FormatSize(state.CurrentSize);

        _updatingOpacity = true;
        var opacityPercent = Math.Round(state.InkOpacity * 100);
        OpacitySlider.Value = opacityPercent;
        OpacityText.Text = $"{opacityPercent:0}%";
        _updatingOpacity = false;

        foreach (var (color, button) in _swatches)
        {
            button.IsChecked = color.R == state.InkColor.R &&
                               color.G == state.InkColor.G &&
                               color.B == state.InkColor.B;
        }

        InkVisibleButton.ToolTip = state.InkVisible
            ? "Hide ink · Ctrl+Shift+1"
            : "Show ink · Ctrl+Shift+1";

        WhiteboardButton.ToolTip = state.Whiteboard switch
        {
            WhiteboardMode.White => "Whiteboard is on · click for blackboard",
            WhiteboardMode.Black => "Blackboard is on · click to turn it off",
            _ => "Whiteboard / blackboard · Ctrl+Shift+W / B"
        };

        FadeButton.ToolTip = state.Fading == FadingMode.Off
            ? "Fading ink is off · Ctrl+Shift+F"
            : $"Fading ink: {state.Fading.DisplayName()} · Ctrl+Shift+F";
    }

    public void ShowFromController(bool raise = true)
    {
        if (!IsVisible)
        {
            Show();
        }

        Opacity = 1;
        if (raise)
        {
            WindowHelper.BringToFront(this);
        }
    }

    public void HideFromController()
    {
        Hide();
    }

    public void BringToFrontNoActivate()
    {
        if (IsVisible)
        {
            WindowHelper.BringToFront(this);
        }
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    public void ResetPosition()
    {
        var monitor = MonitorService.GetPrimaryMonitor();
        var size = GetPhysicalSize();
        var x = monitor.WorkArea.Left + (monitor.WorkArea.Width - size.Width) / 2;
        var y = monitor.WorkArea.Bottom - size.Height - 18;
        var bounds = ClampToWorkArea(new Rect(x, y, size.Width, size.Height));
        WindowHelper.PlaceTopLeftKeepSize(this, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y));
        PersistPosition(bounds);
    }

    public void ResetPositionIfOffscreen()
    {
        var bounds = WindowHelper.GetPhysicalBounds(this);
        var monitors = MonitorService.GetMonitors();
        var visible = monitors.Any(m =>
        {
            var intersection = Rect.Intersect(m.WorkArea, bounds);
            return !intersection.IsEmpty && intersection.Width > 60 && intersection.Height > 30;
        });

        if (!visible)
        {
            ResetPosition();
            return;
        }

        var clamped = ClampToWorkArea(bounds);
        if (Math.Abs(clamped.X - bounds.X) > 0.5 || Math.Abs(clamped.Y - bounds.Y) > 0.5)
        {
            WindowHelper.PlaceTopLeftKeepSize(this, (int)Math.Round(clamped.X), (int)Math.Round(clamped.Y));
            PersistPosition(clamped);
        }
    }

    private void BuildToolMap()
    {
        _toolButtons[ToolKind.PassThrough] = PassButton;
        _toolButtons[ToolKind.Pen] = PenButton;
        _toolButtons[ToolKind.Highlighter] = HighlighterButton;
        _toolButtons[ToolKind.Eraser] = EraserButton;
        _toolButtons[ToolKind.Text] = TextButton;
        _toolButtons[ToolKind.Line] = LineButton;
        _toolButtons[ToolKind.Arrow] = ArrowButton;
        _toolButtons[ToolKind.Rectangle] = RectangleButton;
        _toolButtons[ToolKind.Ellipse] = EllipseButton;
    }

    private void BuildSwatches()
    {
        var style = (Style)FindResource("SwatchToggle");
        foreach (var entry in ColorUtil.Palette.Take(16))
        {
            var brush = new SolidColorBrush(entry.Color);
            brush.Freeze();

            var button = new ToggleButton
            {
                Style = style,
                Background = brush,
                Tag = entry.Color,
                ToolTip = $"{entry.Name} · {entry.Hex}",
                Focusable = false,
                Cursor = Cursors.Hand
            };
            button.Click += Swatch_Click;
            AutomationProperties.SetName(button, entry.Name);
            ToolTipService.SetInitialShowDelay(button, 350);
            QuickColorPanel.Children.Add(button);
            _swatches.Add((entry.Color, button));
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WindowHelper.ApplyNoActivate(this);
        _source = HwndSource.FromHwnd(WindowHelper.Handle(this));
        _source?.AddHook(ToolbarMessageHook);
    }

    private IntPtr ToolbarMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_OPENPEN_SHOW)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(() => _controller.ShowToolbar()));
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_positioned)
        {
            PositionInitial();
            _positioned = true;
        }

        Opacity = 1;
        WindowHelper.BringToFront(this);
    }

    private void PositionInitial()
    {
        var settings = _controller.Settings;
        var size = GetPhysicalSize();
        Rect bounds;

        if (settings.ToolbarX is { } savedX && settings.ToolbarY is { } savedY)
        {
            bounds = ClampToWorkArea(new Rect(savedX, savedY, size.Width, size.Height));
        }
        else
        {
            var monitor = MonitorService.GetPrimaryMonitor();
            var x = monitor.WorkArea.Left + (monitor.WorkArea.Width - size.Width) / 2;
            var y = monitor.WorkArea.Bottom - size.Height - 18;
            bounds = ClampToWorkArea(new Rect(x, y, size.Width, size.Height));
        }

        WindowHelper.PlaceTopLeftKeepSize(this, (int)Math.Round(bounds.X), (int)Math.Round(bounds.Y));
    }

    private Size GetPhysicalSize()
    {
        var scale = MonitorService.GetDpiScaleAt(MonitorService.GetPrimaryMonitor().Center);
        return new Size(Width * scale, Height * scale);
    }

    private Rect ClampToWorkArea(Rect rect)
    {
        var monitors = MonitorService.GetMonitors();
        if (monitors.Count == 0)
        {
            return rect;
        }

        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var monitor = monitors.FirstOrDefault(m => m.WorkArea.Contains(center))
                      ?? monitors.OrderByDescending(m =>
                      {
                          var inter = Rect.Intersect(m.WorkArea, rect);
                          return inter.IsEmpty ? 0 : inter.Width * inter.Height;
                      }).First();

        var work = monitor.WorkArea;
        const double margin = 8;
        var maxX = Math.Max(work.Left + margin, work.Right - rect.Width - margin);
        var maxY = Math.Max(work.Top + margin, work.Bottom - rect.Height - margin);
        var x = Math.Clamp(rect.X, work.Left + margin, maxX);
        var y = Math.Clamp(rect.Y, work.Top + margin, maxY);
        return new Rect(x, y, rect.Width, rect.Height);
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _dragging = true;
        _dragOriginCursor = MonitorService.GetCursorPosition();
        _dragOriginWindow = WindowHelper.GetPhysicalBounds(this);
        DragArea.CaptureMouse();
        e.Handled = true;
    }

    private void DragArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var cursor = MonitorService.GetCursorPosition();
        var dx = cursor.X - _dragOriginCursor.X;
        var dy = cursor.Y - _dragOriginCursor.Y;
        WindowHelper.PlaceTopLeftKeepSize(
            this,
            (int)Math.Round(_dragOriginWindow.X + dx),
            (int)Math.Round(_dragOriginWindow.Y + dy));
        e.Handled = true;
    }

    private void DragArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (DragArea.IsMouseCaptured)
        {
            DragArea.ReleaseMouseCapture();
        }

        var current = WindowHelper.GetPhysicalBounds(this);
        var clamped = ClampToWorkArea(current);
        WindowHelper.PlaceTopLeftKeepSize(this, (int)Math.Round(clamped.X), (int)Math.Round(clamped.Y));
        PersistPosition(clamped);
        e.Handled = true;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && Enum.TryParse<ToolKind>(tag, out var tool))
        {
            _controller.SetTool(tool);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => _controller.Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => _controller.Redo();

    private void Clear_Click(object sender, RoutedEventArgs e) => _controller.ClearInk();

    private void Screenshot_Click(object sender, RoutedEventArgs e) => _ = _controller.TakeScreenshotAsync();

    private void Whiteboard_Click(object sender, RoutedEventArgs e) => _controller.CycleWhiteboard();

    private void Spotlight_Click(object sender, RoutedEventArgs e) => _controller.ToggleSpotlight();

    private void Fade_Click(object sender, RoutedEventArgs e) => _controller.ToggleFading();

    private void InkVisible_Click(object sender, RoutedEventArgs e) => _controller.ToggleInkVisible();

    private void Settings_Click(object sender, RoutedEventArgs e) => _controller.OpenSettings();

    private void CustomColor_Click(object sender, RoutedEventArgs e) => _controller.OpenColorPicker();

    private void SizeDown_Click(object sender, RoutedEventArgs e) => _controller.AdjustSize(-1);

    private void SizeUp_Click(object sender, RoutedEventArgs e) => _controller.AdjustSize(1);

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingOpacity)
        {
            return;
        }

        var percent = Math.Round(e.NewValue);
        OpacityText.Text = $"{percent:0}%";
        _controller.SetOpacity(percent / 100.0);
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => _controller.ToggleToolbar();

    private void ContextHide_Click(object sender, RoutedEventArgs e) => _controller.ToggleToolbar();

    private void ContextSettings_Click(object sender, RoutedEventArgs e) => _controller.OpenSettings();

    private void ContextClose_Click(object sender, RoutedEventArgs e) => _controller.Exit();

    private void BrandMenu_Opened(object sender, RoutedEventArgs e) => _controller.BeginPopup();

    private void BrandMenu_Closed(object sender, RoutedEventArgs e) => _controller.EndPopup();

    private void Expander_Click(object sender, RoutedEventArgs e)
    {
        ApplyExpandedState(!_expanded, keepBottom: true);
    }

    private void ApplyExpandedState(bool expanded, bool keepBottom)
    {
        var bounds = WindowHelper.GetPhysicalBounds(this);
        var scale = WindowHelper.GetDpiScale(this);
        if (scale <= 0.01)
        {
            scale = 1.0;
        }

        _expanded = expanded;
        DetailsRow.Height = expanded ? new GridLength(44) : new GridLength(0);
        DetailsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Height = expanded ? 136 : 92;
        ExpanderButton.IsChecked = expanded;
        ExpanderIcon.RenderTransform = new RotateTransform(expanded ? 180 : 0);
        ExpanderButton.ToolTip = expanded ? "Fewer options" : "More options";
        AutomationProperties.SetName(ExpanderButton, expanded ? "Fewer options" : "More options");

        if (!keepBottom || bounds.Width < 1)
        {
            return;
        }

        var newHeight = Height * scale;
        var rect = new Rect(bounds.X, bounds.Bottom - newHeight, bounds.Width, newHeight);
        var clamped = ClampToWorkArea(rect);
        WindowHelper.PlacePhysical(this, clamped);
        PersistPosition(clamped);
    }

    /// <summary>
    /// The toolbar always opens compact. Persist the position of the collapsed
    /// bar (same bottom edge), so launching again with one row still sits where
    /// the user left it.
    /// </summary>
    private void PersistPosition(Rect currentBounds)
    {
        var scale = WindowHelper.GetDpiScale(this);
        if (scale <= 0.01)
        {
            scale = 1.0;
        }

        var collapsedHeight = 92 * scale;
        var persisted = new Rect(
            currentBounds.X,
            currentBounds.Bottom - collapsedHeight,
            currentBounds.Width,
            collapsedHeight);
        _controller.SaveToolbarPosition(persisted);
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: Color color })
        {
            _controller.SetColor(color);
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        if (_controller.State.ToolbarVisible)
        {
            _controller.ToggleToolbar();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _controller.StateChanged -= OnStateChanged;
        _source?.RemoveHook(ToolbarMessageHook);
        _source = null;
    }

    private static string FormatSize(double size) =>
        Math.Abs(size - Math.Round(size)) < 0.05
            ? $"{(int)Math.Round(size)} px"
            : $"{size:0.#} px";
}
