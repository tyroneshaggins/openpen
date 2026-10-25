using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using OpenPen.Models;
using OpenPen.Views;

namespace OpenPen.Services;

public sealed class AppController : IDisposable
{
    private static readonly int[] QuickColorSlots = { 0, 14, 8, 12, 16, 20 };

    private readonly DispatcherTimer _fadeTimer;
    private readonly DispatcherTimer _spotlightTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _zOrderTimer;

    private HotkeyService? _hotkeys;
    private TrayService? _tray;
    private ToastWindow? _toast;
    private SettingsWindow? _settingsWindow;
    private ColorPickerWindow? _colorWindow;
    private bool _capturing;
    private bool _disposed;
    private double? _toolbarX;
    private double? _toolbarY;
    private FadingMode _lastFadeMode = FadingMode.Medium;
    private long _lastToolbarRaiseTicks;
    private int _dialogDepth;

    public AppSettings Settings { get; }
    public AppState State { get; } = new();
    public InkDocument Document { get; } = new();
    public ToolbarWindow? Toolbar { get; private set; }
    public OverlayManager? Overlays { get; private set; }
    public int FailedHotkeyCount => _hotkeys?.FailedBindings.Count ?? 0;

    public event EventHandler? StateChanged;

    public AppController(AppSettings settings)
    {
        Settings = settings;
        LoadState();

        _fadeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        _fadeTimer.Tick += (_, _) =>
        {
            if (State.Fading != FadingMode.Off)
            {
                Document.TickFade(DateTime.UtcNow);
            }
        };

        _spotlightTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _spotlightTimer.Tick += (_, _) =>
        {
            if (State.SpotlightEnabled)
            {
                Overlays?.UpdateSpotlight(MonitorService.GetCursorPosition());
            }
        };

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };

        // Topmost windows can reorder themselves when clicked. The toolbar must
        // always sit above the ink layer (otherwise an opaque whiteboard would
        // cover it), so cheaply re-assert its position while the overlays are up.
        _zOrderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        _zOrderTimer.Tick += (_, _) =>
        {
            if (State.OverlayShouldBeVisible && State.ToolbarVisible)
            {
                Toolbar?.BringToFrontNoActivate();
            }
        };

        Document.Changed += OnDocumentChanged;
    }

    public void Start()
    {
        Toolbar = new ToolbarWindow(this);
        Overlays = new OverlayManager(this);
        Overlays.Rebuild();

        _tray = new TrayService(this);
        _tray.Initialize();

        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += HandleHotkey;

        if (State.HotkeysEnabled)
        {
            _hotkeys.Enable();
        }

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        ApplyState(scheduleSave: false);

        if (!Settings.FirstRunDone)
        {
            Settings.FirstRunDone = true;
            ScheduleSave();
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowToast("OpenPen is ready", "Ctrl+Shift+3 for the pen  ·  Ctrl+Shift+8 to pass clicks through");
            }), DispatcherPriority.ApplicationIdle);
        }
        else if (_hotkeys.FailedBindings.Count > 0)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowToast(
                    "Some shortcuts are unavailable",
                    $"{_hotkeys.FailedBindings.Count} hotkey(s) are already used by another app. Everything still works from the toolbar.");
            }), DispatcherPriority.ApplicationIdle);
        }
    }

    public void ApplyState(bool scheduleSave = true)
    {
        if (_disposed)
        {
            return;
        }

        Document.FadeLifetime = State.Fading.Lifetime();

        if (State.SpotlightEnabled)
        {
            _spotlightTimer.Start();
        }
        else
        {
            _spotlightTimer.Stop();
        }

        if (State.Fading != FadingMode.Off)
        {
            _fadeTimer.Start();
        }
        else
        {
            _fadeTimer.Stop();
        }

        if (State.ToolbarVisible)
        {
            Toolbar?.ShowFromController(raise: _dialogDepth == 0);
        }
        else
        {
            Toolbar?.HideFromController();
        }

        Overlays?.ApplyState();
        if (_dialogDepth == 0)
        {
            Toolbar?.BringToFrontNoActivate();
        }

        if (State.OverlayShouldBeVisible && State.ToolbarVisible && _dialogDepth == 0)
        {
            _zOrderTimer.Start();
        }
        else
        {
            _zOrderTimer.Stop();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);

        if (scheduleSave)
        {
            ScheduleSave();
        }
    }

    public void SetTool(ToolKind tool)
    {
        if (Document.ActiveStroke is not null)
        {
            Document.CancelActiveStroke();
        }

        State.ActiveTool = tool;

        if (tool == ToolKind.PassThrough)
        {
            State.SpotlightEnabled = false;
            State.Whiteboard = WhiteboardMode.None;
        }
        else
        {
            State.SpotlightEnabled = false;
            State.InkVisible = true;
            if (tool != ToolKind.Eraser && tool != ToolKind.Text)
            {
                State.LastDrawingTool = tool;
            }
            else if (tool == ToolKind.Text)
            {
                State.LastDrawingTool = tool;
            }
        }

        ApplyState();
    }

    public void ToggleDrawingMode()
    {
        if (State.ActiveTool == ToolKind.PassThrough)
        {
            var tool = State.LastDrawingTool;
            if (tool == ToolKind.PassThrough || tool == ToolKind.Eraser)
            {
                tool = ToolKind.Pen;
            }

            SetTool(tool);
        }
        else
        {
            SetTool(ToolKind.PassThrough);
        }
    }

    public void ToggleInkVisible()
    {
        State.InkVisible = !State.InkVisible;
        if (!State.InkVisible)
        {
            Document.CancelActiveStroke();
        }

        ApplyState();
    }

    public void ToggleSpotlight()
    {
        State.SpotlightEnabled = !State.SpotlightEnabled;
        if (State.SpotlightEnabled)
        {
            State.InkVisible = true;
        }

        ApplyState();
    }

    public void CycleWhiteboard()
    {
        var next = State.Whiteboard switch
        {
            WhiteboardMode.None => WhiteboardMode.White,
            WhiteboardMode.White => WhiteboardMode.Black,
            _ => WhiteboardMode.None
        };
        SetWhiteboard(next);
    }

    public void SetWhiteboard(WhiteboardMode mode)
    {
        State.Whiteboard = mode;
        if (mode != WhiteboardMode.None)
        {
            State.InkVisible = true;
            State.SpotlightEnabled = false;
            if (!State.ActiveTool.IsDrawingTool() || State.ActiveTool == ToolKind.Eraser)
            {
                var tool = State.LastDrawingTool;
                if (!tool.IsDrawingTool() || tool == ToolKind.Eraser)
                {
                    tool = ToolKind.Pen;
                }

                State.ActiveTool = tool;
            }

            var luminance = ColorUtil.Luminance(State.InkColor);
            if (mode == WhiteboardMode.White && luminance > 0.72)
            {
                State.LastColor = State.InkColor;
                State.InkColor = Colors.Black;
            }
            else if (mode == WhiteboardMode.Black && luminance < 0.16)
            {
                State.LastColor = State.InkColor;
                State.InkColor = Colors.White;
            }
        }

        ApplyState();
    }

    public void ToggleFading()
    {
        if (State.Fading == FadingMode.Off)
        {
            State.Fading = _lastFadeMode;
            ShowToast("Fading ink on", $"New marks fade out after {FadeSeconds(State.Fading)} seconds.");
        }
        else
        {
            _lastFadeMode = State.Fading;
            State.Fading = FadingMode.Off;
            ShowToast("Fading ink off", "Marks stay on screen until you clear them.");
        }

        ApplyState();
    }

    public void SetFadingMode(FadingMode mode)
    {
        if (mode != FadingMode.Off)
        {
            _lastFadeMode = mode;
        }

        State.Fading = mode;
        ApplyState();
    }

    public void SetSpotlightRadius(double radius)
    {
        State.SpotlightRadius = Math.Clamp(radius, 70, 420);
        ApplyState();
    }

    public void AdjustSize(int direction)
    {
        State.AdjustCurrentSize(direction);
        ApplyState();
    }

    public void SetOpacity(double opacity)
    {
        State.InkOpacity = Math.Clamp(opacity, 0.1, 1.0);
        ScheduleSave();
    }

    public void SetColor(Color color)
    {
        if (State.InkColor.R == color.R && State.InkColor.G == color.G &&
            State.InkColor.B == color.B && State.InkColor.A == color.A)
        {
            return;
        }

        State.LastColor = State.InkColor;
        State.InkColor = color;
        ApplyState();
    }

    public void SwapColor()
    {
        (State.InkColor, State.LastColor) = (State.LastColor, State.InkColor);
        ApplyState();
    }

    public void UseQuickColor(int index)
    {
        if (index < 0 || index >= QuickColorSlots.Length)
        {
            return;
        }

        var slot = QuickColorSlots[index];
        if (slot < ColorUtil.Palette.Count)
        {
            SetColor(ColorUtil.Palette[slot].Color);
        }
    }

    public void Undo()
    {
        Document.Undo();
    }

    public void Redo()
    {
        Document.Redo();
    }

    public void ClearInk()
    {
        if (!Document.HasStrokes && Document.ActiveStroke is null)
        {
            return;
        }

        Document.Clear();
        ShowToast("Ink cleared", "Ctrl+Shift+6 brings it back.");
    }

    public void ToggleToolbar()
    {
        State.ToolbarVisible = !State.ToolbarVisible;
        ApplyState();
    }

    public void ShowToolbar()
    {
        State.ToolbarVisible = true;
        ApplyState();
        if (_dialogDepth == 0)
        {
            Toolbar?.BringToFrontNoActivate();
        }
    }

    public void SaveToolbarPosition(Rect physicalBounds)
    {
        _toolbarX = physicalBounds.X;
        _toolbarY = physicalBounds.Y;
        ScheduleSave();
    }

    public void ResetToolbarPosition()
    {
        Toolbar?.ResetPosition();
        ApplyState();
    }

    public void OpenTextAt(Point physicalPoint)
    {
        var foreground = Interop.NativeMethods.GetForegroundWindow();
        var inkColor = ColorUtil.WithAlpha(State.InkColor, State.InkOpacity);
        var window = new TextEntryWindow(inkColor, State.TextSize, physicalPoint)
        {
            Topmost = true
        };

        var accepted = false;
        var text = string.Empty;
        BeginDialog();
        try
        {
            accepted = window.ShowDialog() == true;
            text = window.ResultText;
        }
        finally
        {
            EndDialog();
        }

        if (accepted && !string.IsNullOrWhiteSpace(text))
        {
            Document.AddText(physicalPoint, text, inkColor, State.TextSize);
        }

        if (foreground != IntPtr.Zero)
        {
            Interop.NativeMethods.SetForegroundWindow(foreground);
        }

        RaiseToolbar();
        ScheduleSave();
    }

    public void OpenColorPicker()
    {
        if (_colorWindow is { IsVisible: true })
        {
            _colorWindow.Activate();
            return;
        }

        var foreground = Interop.NativeMethods.GetForegroundWindow();
        _colorWindow = new ColorPickerWindow(State.InkColor)
        {
            Topmost = true
        };

        BeginDialog();
        try
        {
            if (_colorWindow.ShowDialog() == true && _colorWindow.SelectedColor is { } color)
            {
                SetColor(color);
            }
        }
        finally
        {
            _colorWindow = null;
            EndDialog();
            if (foreground != IntPtr.Zero)
            {
                Interop.NativeMethods.SetForegroundWindow(foreground);
            }

            RaiseToolbar();
        }
    }

    public void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var foreground = Interop.NativeMethods.GetForegroundWindow();
        _settingsWindow = new SettingsWindow(this)
        {
            Topmost = true
        };
        BeginDialog();
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            EndDialog();
            if (foreground != IntPtr.Zero)
            {
                Interop.NativeMethods.SetForegroundWindow(foreground);
            }

            RaiseToolbar();
        };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void BeginPopup() => BeginDialog();

    public void EndPopup() => EndDialog();

    private void BeginDialog()
    {
        _dialogDepth++;
        _zOrderTimer.Stop();
    }

    private void EndDialog()
    {
        if (_disposed)
        {
            return;
        }

        _dialogDepth = Math.Max(0, _dialogDepth - 1);
        if (_dialogDepth == 0 && State.OverlayShouldBeVisible && State.ToolbarVisible)
        {
            _zOrderTimer.Start();
            RaiseToolbar();
        }
    }

    public async Task TakeScreenshotAsync()
    {
        if (_capturing)
        {
            return;
        }

        _capturing = true;
        var toolbarWasVisible = Toolbar?.IsVisible == true;
        string title;
        string message;
        try
        {
            Toolbar?.HideFromController();
            _toast?.HideNow();
            await Task.Delay(140);

            var result = ScreenshotService.CaptureAndSave();
            title = "Screenshot copied";
            message = $"Saved to {Path.GetFileName(Path.GetDirectoryName(result.FilePath))}\\{Path.GetFileName(result.FilePath)}";
        }
        catch (Exception ex)
        {
            title = "Screenshot failed";
            message = ex.Message;
        }
        finally
        {
            if (toolbarWasVisible)
            {
                Toolbar?.ShowFromController();
            }

            _capturing = false;
            RaiseToolbar();
        }

        ShowToast(title, message);
    }

    public void SetHotkeysEnabled(bool enabled)
    {
        State.HotkeysEnabled = enabled;
        if (enabled)
        {
            _hotkeys?.Enable();
            if (_hotkeys is { FailedBindings.Count: > 0 })
            {
                ShowToast("Some shortcuts are unavailable", "Another app is already using them. The toolbar still works.");
            }
        }
        else
        {
            _hotkeys?.Disable();
        }

        ApplyState();
    }

    public void SetStartWithWindows(bool enabled)
    {
        if (!StartupService.SetEnabled(enabled))
        {
            ShowToast("Could not update startup", "Windows blocked the change. Try running OpenPen once more, then retry.");
            return;
        }

        State.StartWithWindows = enabled;
        ApplyState();
    }

    public void RaiseToolbar()
    {
        if (_dialogDepth > 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _lastToolbarRaiseTicks < 90)
        {
            return;
        }

        _lastToolbarRaiseTicks = now;
        Toolbar?.BringToFrontNoActivate();
    }

    public void ShowToast(string title, string message)
    {
        _toast ??= new ToastWindow { Topmost = true };
        Rect? avoid = State.ToolbarVisible && Toolbar is not null
            ? WindowHelper.GetPhysicalBounds(Toolbar)
            : null;
        _toast.ShowMessage(title, message, avoid);
    }

    public void SaveNow()
    {
        SyncSettings();
        Settings.Save();
    }

    public void Exit()
    {
        if (Application.Current is { } app)
        {
            app.Shutdown();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _saveTimer.Stop();
        _fadeTimer.Stop();
        _spotlightTimer.Stop();
        _zOrderTimer.Stop();
        SaveNow();

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Document.Changed -= OnDocumentChanged;

        if (_hotkeys is not null)
        {
            _hotkeys.Pressed -= HandleHotkey;
            _hotkeys.Dispose();
            _hotkeys = null;
        }

        _toast?.HideNow();
        _toast?.Close();
        _toast = null;

        _settingsWindow?.Close();
        _settingsWindow = null;
        _colorWindow?.Close();
        _colorWindow = null;

        _tray?.Dispose();
        _tray = null;

        Overlays?.Dispose();
        Overlays = null;

        Toolbar?.CloseForExit();
        Toolbar = null;
    }

    private void LoadState()
    {
        State.ActiveTool = ToolKind.PassThrough;
        State.LastDrawingTool = Settings.LastDrawingTool.IsDrawingTool() && Settings.LastDrawingTool != ToolKind.Eraser
            ? Settings.LastDrawingTool
            : ToolKind.Pen;
        State.InkColor = Settings.InkMediaColor;
        State.LastColor = Settings.LastMediaColor;
        State.PenSize = Settings.PenSize;
        State.HighlighterSize = Settings.HighlighterSize;
        State.EraserSize = Settings.EraserSize;
        State.ShapeSize = Settings.ShapeSize;
        State.TextSize = Settings.TextSize;
        State.InkOpacity = Math.Clamp(Settings.InkOpacity, 0.1, 1.0);
        State.SpotlightRadius = Settings.SpotlightRadius;
        State.Fading = Settings.Fading;
        State.ToolbarVisible = Settings.ToolbarVisible;
        State.HotkeysEnabled = Settings.HotkeysEnabled;
        State.StartWithWindows = StartupService.IsEnabled();
        State.InkVisible = true;

        if (State.Fading != FadingMode.Off)
        {
            _lastFadeMode = State.Fading;
        }

        Settings.StartWithWindows = State.StartWithWindows;
        _toolbarX = Settings.ToolbarX;
        _toolbarY = Settings.ToolbarY;
    }

    private void SyncSettings()
    {
        Settings.InkColor = ColorUtil.ToHex(State.InkColor);
        Settings.LastColor = ColorUtil.ToHex(State.LastColor);
        Settings.PenSize = State.PenSize;
        Settings.HighlighterSize = State.HighlighterSize;
        Settings.EraserSize = State.EraserSize;
        Settings.ShapeSize = State.ShapeSize;
        Settings.TextSize = State.TextSize;
        Settings.InkOpacity = State.InkOpacity;
        Settings.SpotlightRadius = State.SpotlightRadius;
        Settings.Fading = State.Fading;
        Settings.ToolbarVisible = State.ToolbarVisible;
        Settings.HotkeysEnabled = State.HotkeysEnabled;
        Settings.StartWithWindows = State.StartWithWindows;
        Settings.LastDrawingTool = State.LastDrawingTool;
        Settings.ToolbarX = _toolbarX;
        Settings.ToolbarY = _toolbarY;
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        Toolbar?.Refresh();
        _tray?.Refresh();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            Overlays?.Rebuild();
            ApplyState();
            Toolbar?.ResetPositionIfOffscreen();
        }), DispatcherPriority.Background);
    }

    private void HandleHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleInk:
                ToggleInkVisible();
                break;
            case HotkeyAction.Spotlight:
                ToggleSpotlight();
                break;
            case HotkeyAction.Pen:
                SetTool(ToolKind.Pen);
                break;
            case HotkeyAction.Highlighter:
                SetTool(ToolKind.Highlighter);
                break;
            case HotkeyAction.Eraser:
                SetTool(ToolKind.Eraser);
                break;
            case HotkeyAction.Undo:
                Undo();
                break;
            case HotkeyAction.Redo:
                Redo();
                break;
            case HotkeyAction.Clear:
                ClearInk();
                break;
            case HotkeyAction.Screenshot:
                _ = TakeScreenshotAsync();
                break;
            case HotkeyAction.SizeDown:
                AdjustSize(-1);
                break;
            case HotkeyAction.SizeUp:
                AdjustSize(1);
                break;
            case HotkeyAction.Line:
                SetTool(ToolKind.Line);
                break;
            case HotkeyAction.Ellipse:
                SetTool(ToolKind.Ellipse);
                break;
            case HotkeyAction.Rectangle:
                SetTool(ToolKind.Rectangle);
                break;
            case HotkeyAction.Arrow:
                SetTool(ToolKind.Arrow);
                break;
            case HotkeyAction.Text:
                SetTool(ToolKind.Text);
                break;
            case HotkeyAction.Whiteboard:
                SetWhiteboard(State.Whiteboard == WhiteboardMode.White ? WhiteboardMode.None : WhiteboardMode.White);
                break;
            case HotkeyAction.Blackboard:
                SetWhiteboard(State.Whiteboard == WhiteboardMode.Black ? WhiteboardMode.None : WhiteboardMode.Black);
                break;
            case HotkeyAction.Fading:
                ToggleFading();
                break;
            case HotkeyAction.ToggleToolbar:
                ToggleToolbar();
                break;
            case HotkeyAction.LastColor:
                SwapColor();
                break;
            case HotkeyAction.PassThrough:
                SetTool(ToolKind.PassThrough);
                break;
            case HotkeyAction.Color1:
                UseQuickColor(0);
                break;
            case HotkeyAction.Color2:
                UseQuickColor(1);
                break;
            case HotkeyAction.Color3:
                UseQuickColor(2);
                break;
            case HotkeyAction.Color4:
                UseQuickColor(3);
                break;
            case HotkeyAction.Color5:
                UseQuickColor(4);
                break;
            case HotkeyAction.Color6:
                UseQuickColor(5);
                break;
        }
    }

    private static int FadeSeconds(FadingMode mode) => mode switch
    {
        FadingMode.Slow => 20,
        FadingMode.Fast => 5,
        FadingMode.Medium => 10,
        _ => 0
    };
}
