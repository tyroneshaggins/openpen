using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OpenPen.Services;

namespace OpenPen.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private bool _isHiding;
    private Rect? _avoid;

    public ToastWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3.4)
        };
        _hideTimer.Tick += (_, _) => BeginHide();

        SourceInitialized += (_, _) =>
        {
            WindowHelper.ApplyNoActivate(this);
            WindowHelper.SetClickThrough(this, true);
        };
    }

    public void ShowMessage(string title, string message, Rect? avoid = null)
    {
        _isHiding = false;
        _avoid = avoid;
        _hideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        TitleText.Text = title;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        Position();
        Topmost = true;
        WindowHelper.BringToFront(this);
        _hideTimer.Start();
    }

    public void HideNow()
    {
        _hideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        _isHiding = false;
        Hide();
    }

    private void BeginHide()
    {
        if (_isHiding)
        {
            return;
        }

        _isHiding = true;
        _hideTimer.Stop();
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            if (_isHiding)
            {
                Hide();
                BeginAnimation(OpacityProperty, null);
                Opacity = 1;
                _isHiding = false;
            }
        };
        BeginAnimation(OpacityProperty, animation);
    }

    private void Position()
    {
        var monitor = MonitorService.GetPrimaryMonitor();
        var bounds = WindowHelper.GetPhysicalBounds(this);
        var width = bounds.Width > 1 ? bounds.Width : Width;
        var height = bounds.Height > 1 ? bounds.Height : 80;
        var x = monitor.WorkArea.Right - width - 12;
        var y = monitor.WorkArea.Bottom - height - 12;

        if (_avoid is { } avoid)
        {
            var candidate = new Rect(x, y, width, height);
            var padded = avoid;
            padded.Inflate(8, 8);
            if (candidate.IntersectsWith(padded))
            {
                y = avoid.Top - height - 10;
                if (y < monitor.WorkArea.Top + 8)
                {
                    y = monitor.WorkArea.Top + 8;
                }
            }
        }

        WindowHelper.PlaceTopLeftKeepSize(this, (int)Math.Round(x), (int)Math.Round(y));
    }
}
