using System.Threading;
using System.Windows;
using System.Windows.Threading;
using OpenPen.Interop;
using OpenPen.Services;

namespace OpenPen;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\OpenPen.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // A hidden toolbar still owns an HWND, so a direct message is the
            // most reliable way to ask the running instance to reveal itself.
            var toolbar = NativeMethods.FindWindow(null, "OpenPen");
            if (toolbar != IntPtr.Zero)
            {
                NativeMethods.PostMessage(toolbar, NativeMethods.WM_OPENPEN_SHOW, IntPtr.Zero, IntPtr.Zero);
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log(args.ExceptionObject as Exception);

        try
        {
            var settings = AppSettings.Load();
            _controller = new AppController(settings);
            _controller.Start();
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show(
                "OpenPen could not start.\n\n" + ex.Message,
                "OpenPen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _controller?.Dispose();
        }
        catch (Exception ex)
        {
            Log(ex);
        }

        try
        {
            _singleInstance?.ReleaseMutex();
            _singleInstance?.Dispose();
        }
        catch
        {
            // ignored
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "OpenPen hit an unexpected error, but it is still running.\n\n" + e.Exception.Message,
            "OpenPen",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void Log(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppSettings.SettingsDirectory);
            File.AppendAllText(
                Path.Combine(AppSettings.SettingsDirectory, "openpen.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never be the reason the app fails.
        }
    }
}
