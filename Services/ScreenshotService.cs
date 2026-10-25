using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using Drawing = System.Drawing;

namespace OpenPen.Services;

public sealed record ScreenshotResult(string FilePath, int Width, int Height);

public static class ScreenshotService
{
    public static ScreenshotResult CaptureAndSave()
    {
        var bounds = MonitorService.GetVirtualBounds();
        var width = Math.Max(1, (int)Math.Round(bounds.Width));
        var height = Math.Max(1, (int)Math.Round(bounds.Height));

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                (int)Math.Round(bounds.X),
                (int)Math.Round(bounds.Y),
                0,
                0,
                new Drawing.Size(width, height),
                CopyPixelOperation.SourceCopy);
        }

        var folder = GetScreenshotFolder();
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(folder, $"OpenPen_{stamp}.png");
        var counter = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(folder, $"OpenPen_{stamp}_{counter++}.png");
        }

        bitmap.Save(path, ImageFormat.Png);
        CopyToClipboard(bitmap);
        return new ScreenshotResult(path, width, height);
    }

    public static string GetScreenshotFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        return Path.Combine(pictures, "OpenPen");
    }

    private static void CopyToClipboard(Bitmap bitmap)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetImage(bitmap);
                return;
            }
            catch
            {
                Thread.Sleep(40);
            }
        }
    }
}
