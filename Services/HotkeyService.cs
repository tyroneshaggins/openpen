using System.Windows.Input;
using System.Windows.Interop;
using OpenPen.Interop;

namespace OpenPen.Services;

public enum HotkeyAction
{
    ToggleInk,
    Spotlight,
    Pen,
    Highlighter,
    Eraser,
    Undo,
    Redo,
    Clear,
    Screenshot,
    SizeDown,
    SizeUp,
    Line,
    Ellipse,
    Rectangle,
    Arrow,
    Text,
    Whiteboard,
    Blackboard,
    Fading,
    ToggleToolbar,
    LastColor,
    PassThrough,
    Color1,
    Color2,
    Color3,
    Color4,
    Color5,
    Color6
}

public sealed record HotkeyBinding(
    HotkeyAction Action,
    uint Modifiers,
    Key Key,
    string Display,
    string Description);

public sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private readonly List<HotkeyBinding> _failed = new();
    private HwndSource? _source;
    private int _nextId = 1;
    private bool _enabled;

    public event Action<HotkeyAction>? Pressed;

    public IReadOnlyList<HotkeyBinding> FailedBindings => _failed;

    public static IReadOnlyList<HotkeyBinding> Defaults { get; } = new HotkeyBinding[]
    {
        new(HotkeyAction.ToggleInk, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D1, "Ctrl + Shift + 1", "Show or hide ink"),
        new(HotkeyAction.Spotlight, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D2, "Ctrl + Shift + 2", "Spotlight cursor"),
        new(HotkeyAction.Pen, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D3, "Ctrl + Shift + 3", "Pen"),
        new(HotkeyAction.Highlighter, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D4, "Ctrl + Shift + 4", "Highlighter"),
        new(HotkeyAction.Eraser, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D5, "Ctrl + Shift + 5", "Eraser"),
        new(HotkeyAction.Undo, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D6, "Ctrl + Shift + 6", "Undo"),
        new(HotkeyAction.Clear, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D7, "Ctrl + Shift + 7", "Clear all ink"),
        new(HotkeyAction.PassThrough, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D8, "Ctrl + Shift + 8", "Pass clicks through"),
        new(HotkeyAction.LastColor, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D9, "Ctrl + Shift + 9", "Swap to your last color"),
        new(HotkeyAction.ToggleToolbar, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.D0, "Ctrl + Shift + 0", "Show or hide the toolbar"),
        new(HotkeyAction.Screenshot, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.OemComma, "Ctrl + Shift + ,", "Screenshot to clipboard"),
        new(HotkeyAction.SizeUp, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.OemCloseBrackets, "Ctrl + Shift + ]", "Increase tool size"),
        new(HotkeyAction.SizeDown, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.OemOpenBrackets, "Ctrl + Shift + [", "Decrease tool size"),
        new(HotkeyAction.Line, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.L, "Ctrl + Shift + L", "Line"),
        new(HotkeyAction.Arrow, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.A, "Ctrl + Shift + A", "Arrow"),
        new(HotkeyAction.Rectangle, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.R, "Ctrl + Shift + R", "Rectangle"),
        new(HotkeyAction.Ellipse, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.E, "Ctrl + Shift + E", "Ellipse"),
        new(HotkeyAction.Text, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.T, "Ctrl + Shift + T", "Text"),
        new(HotkeyAction.Whiteboard, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.W, "Ctrl + Shift + W", "Whiteboard"),
        new(HotkeyAction.Blackboard, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.B, "Ctrl + Shift + B", "Blackboard"),
        new(HotkeyAction.Fading, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.F, "Ctrl + Shift + F", "Fading ink"),
        new(HotkeyAction.Redo, NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT, Key.Z, "Ctrl + Shift + Z", "Redo"),
        new(HotkeyAction.Color1, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key.D1, "Ctrl + Alt + 1", "Quick color 1"),
        new(HotkeyAction.Color2, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key.D2, "Ctrl + Alt + 2", "Quick color 2"),
        new(HotkeyAction.Color3, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key.D3, "Ctrl + Alt + 3", "Quick color 3"),
        new(HotkeyAction.Color4, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key.D4, "Ctrl + Alt + 4", "Quick color 4"),
        new(HotkeyAction.Color5, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key.D5, "Ctrl + Alt + 5", "Quick color 5"),
        new(HotkeyAction.Color6, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Key.D6, "Ctrl + Alt + 6", "Quick color 6")
    };

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("OpenPen.Hotkeys")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public void Enable()
    {
        if (_enabled || _source is null)
        {
            return;
        }

        _registered.Clear();
        _failed.Clear();

        foreach (var binding in Defaults)
        {
            var id = _nextId++;
            var vk = (uint)KeyInterop.VirtualKeyFromKey(binding.Key);
            if (vk == 0)
            {
                _failed.Add(binding);
                continue;
            }

            if (NativeMethods.RegisterHotKey(_source.Handle, id, binding.Modifiers | NativeMethods.MOD_NOREPEAT, vk))
            {
                _registered[id] = binding.Action;
            }
            else
            {
                _failed.Add(binding);
            }
        }

        _enabled = true;
    }

    public void Disable()
    {
        if (!_enabled || _source is null)
        {
            return;
        }

        foreach (var id in _registered.Keys)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        }

        _registered.Clear();
        _enabled = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_registered.TryGetValue(id, out var action))
            {
                handled = true;
                Pressed?.Invoke(action);
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Disable();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
