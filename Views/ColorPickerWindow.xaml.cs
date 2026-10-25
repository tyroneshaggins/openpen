using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OpenPen.Services;

namespace OpenPen.Views;

public partial class ColorPickerWindow : Window
{
    private readonly List<ToggleButton> _swatches = new();
    private Color _color;
    private bool _updating;

    public Color? SelectedColor { get; private set; }

    public ColorPickerWindow(Color initialColor)
    {
        InitializeComponent();
        BuildSwatches();
        SetColor(initialColor, updateChecks: true);
        ContentRendered += (_, _) =>
        {
            WindowHelper.ForceForeground(this);
            Activate();
            HexBox.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
        };
    }

    private void BuildSwatches()
    {
        var style = (Style)FindResource("PaletteSwatch");
        foreach (var entry in ColorUtil.Palette)
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
            button.Click += (_, _) =>
            {
                if (button.Tag is Color color)
                {
                    SetColor(color, updateChecks: true);
                }
            };
            AutomationProperties.SetName(button, entry.Name);
            ToolTipService.SetInitialShowDelay(button, 350);
            SwatchPanel.Children.Add(button);
            _swatches.Add(button);
        }
    }

    private void SetColor(Color color, bool updateChecks)
    {
        _updating = true;
        _color = color;
        Preview.Background = new SolidColorBrush(color);
        if (!HexBox.IsKeyboardFocusWithin)
        {
            HexBox.Text = ColorUtil.ToHex(color);
        }

        RedSlider.Value = color.R;
        GreenSlider.Value = color.G;
        BlueSlider.Value = color.B;
        RedValue.Text = color.R.ToString();
        GreenValue.Text = color.G.ToString();
        BlueValue.Text = color.B.ToString();

        if (updateChecks)
        {
            foreach (var swatch in _swatches)
            {
                swatch.IsChecked = swatch.Tag is Color swatchColor &&
                                   swatchColor.R == color.R &&
                                   swatchColor.G == color.G &&
                                   swatchColor.B == color.B;
            }
        }

        _updating = false;
    }

    private void Rgb_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating)
        {
            return;
        }

        var color = Color.FromRgb((byte)Math.Round(RedSlider.Value), (byte)Math.Round(GreenSlider.Value),
            (byte)Math.Round(BlueSlider.Value));
        SetColor(color, updateChecks: true);
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        if (ColorUtil.TryParse(HexBox.Text, out var color))
        {
            SetColor(color, updateChecks: true);
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ColorUtil.TryParse(HexBox.Text, out var color))
        {
            SetColor(color, updateChecks: true);
        }

        SelectedColor = _color;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
