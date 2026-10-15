using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickDescribe.Tests;

/// <summary>Test pictures, drawn with WPF on a thread of their own (WPF drawing needs a single-threaded apartment).</summary>
internal static class Drawn
{
    public static T OnSta<T>(Func<T> work)
    {
        T? result = default;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
        return result!;
    }

    /// <summary>Black lines of text on white, like a window.</summary>
    public static BitmapSource Text(int width, int height, double size, params string[] lines) => OnSta<BitmapSource>(() =>
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var y = 20.0;
            foreach (var line in lines)
            {
                var text = new FormattedText(line, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), size, Brushes.Black, 1.0);
                dc.DrawText(text, new Point(20, y));
                y += size * 1.6;
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    });

    /// <summary>A solid picture, optionally see-through (premultiplied, as WPF keeps it).</summary>
    public static BitmapSource Solid(int width, int height, byte b, byte g, byte r, byte alpha = 255)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(b * alpha / 255);
            pixels[i + 1] = (byte)(g * alpha / 255);
            pixels[i + 2] = (byte)(r * alpha / 255);
            pixels[i + 3] = alpha;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static string SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }
}
