using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenPen.Services;

namespace OpenPen.Views;

public partial class TextEntryWindow : Window
{
    private readonly Point _anchorPhysical;

    public string ResultText { get; private set; } = string.Empty;

    public TextEntryWindow(Color color, double fontSize, Point physicalPoint)
    {
        InitializeComponent();
        _anchorPhysical = physicalPoint;

        ColorDot.Background = new SolidColorBrush(color);
        SizeLabel.Text = $"{Math.Round(fontSize):0} px";
        InputText.FontSize = Math.Clamp(fontSize * 0.65, 14, 24);

        ContentRendered += (_, _) =>
        {
            Position();
            WindowHelper.ForceForeground(this);
            Activate();
            InputText.Focus();
            Keyboard.Focus(InputText);
        };

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            Commit();
            e.Handled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e) => Commit();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Commit()
    {
        var text = InputText.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            DialogResult = false;
            return;
        }

        ResultText = text;
        DialogResult = true;
    }

    private void Position()
    {
        var monitor = MonitorService.GetMonitorAt(_anchorPhysical) ?? MonitorService.GetPrimaryMonitor();
        UpdateLayout();
        var size = WindowHelper.GetPhysicalBounds(this);
        if (size.Width < 1 || size.Height < 1)
        {
            size = new Rect(0, 0, Width, ActualHeight);
        }

        var x = _anchorPhysical.X + 14;
        var y = _anchorPhysical.Y + 14;

        if (x + size.Width > monitor.WorkArea.Right - 8)
        {
            x = monitor.WorkArea.Right - size.Width - 8;
        }

        if (y + size.Height > monitor.WorkArea.Bottom - 8)
        {
            y = _anchorPhysical.Y - size.Height - 10;
        }

        x = Math.Max(monitor.WorkArea.Left + 8, x);
        y = Math.Max(monitor.WorkArea.Top + 8, y);
        WindowHelper.PlaceTopLeftKeepSize(this, (int)Math.Round(x), (int)Math.Round(y));
    }
}
