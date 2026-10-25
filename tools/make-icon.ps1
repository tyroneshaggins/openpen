Add-Type -AssemblyName System.Drawing

$source = @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class OpenPenIconMaker
{
    static Bitmap Make(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float pad = Math.Max(0f, s * 0.035f);
            float r = s * 0.24f;
            var rect = new RectangleF(pad, pad, s - 2 * pad, s - 2 * pad);
            using (var path = RoundRect(rect, r))
            using (var brush = new LinearGradientBrush(rect, Color.FromArgb(255, 255, 146, 90), Color.FromArgb(255, 255, 94, 38), 45f))
                g.FillPath(brush, path);

            var pts = new PointF[]
            {
                new PointF(s * 0.33f, s * 0.17f),
                new PointF(s * 0.67f, s * 0.17f),
                new PointF(s * 0.63f, s * 0.60f),
                new PointF(s * 0.50f, s * 0.83f),
                new PointF(s * 0.37f, s * 0.60f)
            };
            using (var nib = new GraphicsPath())
            {
                nib.AddPolygon(pts);
                using (var shadow = new SolidBrush(Color.FromArgb(70, 80, 20, 0)))
                {
                    g.TranslateTransform(s * 0.018f, s * 0.022f);
                    g.FillPath(shadow, nib);
                    g.ResetTransform();
                }
                using (var white = new LinearGradientBrush(rect, Color.White, Color.FromArgb(255, 255, 236, 224), 90f))
                    g.FillPath(white, nib);
            }

            float holeR = s * 0.085f;
            using (var holeBrush = new SolidBrush(Color.FromArgb(255, 255, 106, 46)))
                g.FillEllipse(holeBrush, s * 0.5f - holeR, s * 0.4f - holeR, holeR * 2, holeR * 2);
            using (var pen = new Pen(Color.FromArgb(255, 255, 106, 46), Math.Max(1f, s * 0.045f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, s * 0.5f, s * 0.48f, s * 0.5f, s * 0.77f);
            }
        }
        return bmp;
    }

    static GraphicsPath RoundRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Build(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var pngs = new System.Collections.Generic.List<byte[]>();
        foreach (int s in sizes)
        {
            using (var bmp = Make(s))
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                pngs.Add(ms.ToArray());
            }
        }

        string ico = Path.Combine(root, "Assets", "openpen.ico");
        using (var fs = File.Create(ico))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                byte dim = (byte)(s >= 256 ? 0 : s);
                bw.Write(dim); bw.Write(dim); bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((ushort)1); bw.Write((ushort)32);
                bw.Write((uint)pngs[i].Length); bw.Write((uint)offset);
                offset += pngs[i].Length;
            }
            foreach (var bytes in pngs) bw.Write(bytes);
        }

        using (var bmp = Make(256)) bmp.Save(Path.Combine(root, "Assets", "openpen-256.png"), ImageFormat.Png);
        using (var bmp = Make(32)) bmp.Save(Path.Combine(root, "Assets", "openpen-32.png"), ImageFormat.Png);
        Console.WriteLine("wrote " + ico);
    }
}
"@

Add-Type -TypeDefinition $source -ReferencedAssemblies System.Drawing

$root = Split-Path -Parent $PSScriptRoot
[OpenPenIconMaker]::Build($root)
