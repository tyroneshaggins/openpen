using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenPen.Models;
using OpenPen.Services;

namespace OpenPen.Views;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        BuildHotkeys();
        RefreshFromState();
        _controller.StateChanged += OnStateChanged;
        Closed += (_, _) => _controller.StateChanged -= OnStateChanged;
        ContentRendered += (_, _) =>
        {
            WindowHelper.ForceForeground(this);
            Activate();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    private void BuildHotkeys()
    {
        var textBrush = (Brush)FindResource("TextBrush");
        var mutedBrush = (Brush)FindResource("TextMutedBrush");
        var keycapStyle = (Style)FindResource("Keycap");
        var keyTextStyle = (Style)FindResource("KeycapText");

        foreach (var binding in HotkeyService.Defaults)
        {
            var keyText = new TextBlock
            {
                Text = binding.Display,
                Style = keyTextStyle
            };
            var keycap = new Border
            {
                Style = keycapStyle,
                Child = keyText
            };
            DockPanel.SetDock(keycap, Dock.Right);

            var description = new TextBlock
            {
                Text = binding.Description,
                Foreground = textBrush,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 6, 0)
            };

            var dock = new DockPanel { LastChildFill = true };
            dock.Children.Add(keycap);
            dock.Children.Add(description);

            var item = new Border
            {
                Width = 296,
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                Child = dock
            };

            HotkeyPanel.Children.Add(item);
        }
    }

    private void RefreshFromState()
    {
        var state = _controller.State;
        StartupToggle.IsChecked = state.StartWithWindows;
        HotkeysToggle.IsChecked = state.HotkeysEnabled;
        FadeOffButton.IsChecked = state.Fading == FadingMode.Off;
        FadeSlowButton.IsChecked = state.Fading == FadingMode.Slow;
        FadeMediumButton.IsChecked = state.Fading == FadingMode.Medium;
        FadeFastButton.IsChecked = state.Fading == FadingMode.Fast;

        var radius = state.SpotlightRadius;
        var nearest = new[] { 120.0, 170.0, 230.0 }.OrderBy(v => Math.Abs(v - radius)).First();
        SpotlightSmallButton.IsChecked = Math.Abs(nearest - 120) < 0.1;
        SpotlightMediumButton.IsChecked = Math.Abs(nearest - 170) < 0.1;
        SpotlightLargeButton.IsChecked = Math.Abs(nearest - 230) < 0.1;

        if (!state.HotkeysEnabled)
        {
            HotkeyWarning.Visibility = Visibility.Visible;
            HotkeyWarning.Text = "Global shortcuts are off. Use the toolbar or the tray menu.";
        }
        else if (_controller.FailedHotkeyCount > 0)
        {
            HotkeyWarning.Visibility = Visibility.Visible;
            HotkeyWarning.Text =
                $"{_controller.FailedHotkeyCount} shortcut(s) could not be registered — another app is using them.";
        }
        else
        {
            HotkeyWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e) => RefreshFromState();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // ignored
            }
        }
    }

    private void StartupToggle_Click(object sender, RoutedEventArgs e)
    {
        _controller.SetStartWithWindows(StartupToggle.IsChecked == true);
        RefreshFromState();
    }

    private void HotkeysToggle_Click(object sender, RoutedEventArgs e)
    {
        _controller.SetHotkeysEnabled(HotkeysToggle.IsChecked == true);
        RefreshFromState();
    }

    private void FadeMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton { Tag: string tag } &&
            Enum.TryParse<FadingMode>(tag, out var mode))
        {
            _controller.SetFadingMode(mode);
            RefreshFromState();
        }
    }

    private void SpotlightSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton { Tag: string tag } &&
            double.TryParse(tag, out var size))
        {
            _controller.SetSpotlightRadius(size);
            RefreshFromState();
        }
    }

    private void ClearInk_Click(object sender, RoutedEventArgs e) => _controller.ClearInk();

    private void ResetToolbar_Click(object sender, RoutedEventArgs e) => _controller.ResetToolbarPosition();

    private void Exit_Click(object sender, RoutedEventArgs e) => _controller.Exit();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
