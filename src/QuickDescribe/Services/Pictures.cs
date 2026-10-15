using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickDescribe.Services;

/// <summary>Reads pictures from files and the clipboard, and turns them into what the describer and OCR take.</summary>
public static class Pictures
{
    /// <summary>The file types offered in Open Picture and taken from the clipboard or a drop. Windows reads HEIC, HEIF and
    /// WebP when their free extensions from the Microsoft Store are installed, as they are on most PCs.</summary>
    public static readonly string[] Extensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".heif", ".webp", ".jxr", ".wdp", ".ico"];

    public static string OpenFilter =>
        $"Pictures|{string.Join(";", Extensions.Select(e => "*" + e))}|All files|*.*";

    public static bool IsPictureFile(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>The first picture file in a list of files (a drop, or files copied in File Explorer).</summary>
    public static string? FirstPictureFile(IEnumerable<string> files) => files.FirstOrDefault(IsPictureFile);

    /// <summary>Reads a picture file, turned the way it was taken (phones save many photos sideways with a note to turn
    /// them). Throws with a readable message when it can't.</summary>
    public static Picture FromFile(string path)
    {
        BitmapSource image;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            image = Oriented(frame, OrientationOf(frame));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException
                                       or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new PictureException($"{Path.GetFileName(path)} couldn't be read as a picture" +
                                       (ex is NotSupportedException ? ": Windows can't read this type of file." : $": {ex.Message.Trim()}"));
        }
        if (image.CanFreeze) image.Freeze();
        return new Picture(image, Path.GetFileName(path));
    }

    /// <summary>A picture on the clipboard: a copied picture file, a picture copied as PNG (browsers, Office), or as a
    /// bitmap (screenshots). Call on the UI thread.</summary>
    public static Picture FromClipboard()
    {
        IDataObject? data;
        try { data = Clipboard.GetDataObject(); }
        catch (System.Runtime.InteropServices.ExternalException) { throw new PictureException("The clipboard is busy. Try again."); }
        if (data is null) throw new PictureException("The clipboard is empty.");

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var file = FirstPictureFile(files) ?? throw new PictureException("The files on the clipboard aren't pictures.");
            var picture = FromFile(file);
            return picture with { Source = $"Clipboard, {picture.Source}" };
        }
        if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
        {
            try
            {
                var frame = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                frame.Freeze();
                return new Picture(frame, "Clipboard");
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
            {
                // Fall through to the bitmap the same app usually puts there too.
            }
        }
        if (data.GetDataPresent(DataFormats.Bitmap) && Clipboard.GetImage() is { } bitmap)
        {
            // The clipboard's bitmaps have no real transparency: Bgr32 keeps an unset alpha byte from blanking the picture.
            var opaque = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            return new Picture(opaque, "Clipboard");
        }
        throw new PictureException(data.GetDataPresent(DataFormats.UnicodeText)
            ? "The clipboard has text, not a picture."
            : "There's no picture on the clipboard.");
    }

    /// <summary>The EXIF orientation, 1 to 8; 1 (as it is) when the file doesn't say.</summary>
    private static int OrientationOf(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort value && value is >= 1 and <= 8)
                return value;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // Formats without that metadata.
        }
        return 1;
    }

    /// <summary>Turns a picture by its EXIF orientation.</summary>
    internal static BitmapSource Oriented(BitmapSource image, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => null,
        };
        return transform is null ? image : new TransformedBitmap(image, transform);
    }

    /// <summary>The picture's pixels on white, top to bottom, 4 bytes each in BGRA order with alpha 255: what the
    /// describer and OCR take. Transparent parts become white rather than black, as a transparent logo looks on a page.</summary>
    public static (byte[] Pixels, int Width, int Height) OpaquePixels(BitmapSource image, double scale = 1)
    {
        BitmapSource source = image;
        if (Math.Abs(scale - 1) > 0.001)
            source = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        var premultiplied = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var width = premultiplied.PixelWidth;
        var height = premultiplied.PixelHeight;
        var pixels = new byte[width * height * 4];
        premultiplied.CopyPixels(pixels, width * 4, 0);
        // A source with no alpha (Bgr32, Bgr24) arrives with alpha 255 already, so this changes nothing for it.
        OnWhite(pixels);
        return (pixels, width, height);
    }

    /// <summary>Composites premultiplied BGRA pixels onto white, in place.</summary>
    internal static void OnWhite(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var clear = 255 - pixels[i + 3];
            if (clear == 0) continue;
            pixels[i] = (byte)Math.Min(255, pixels[i] + clear);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + clear);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + clear);
            pixels[i + 3] = 255;
        }
    }

    /// <summary>The scale that fits the longer side within <paramref name="maxSide"/>; never more than 1.</summary>
    internal static double FitScale(int width, int height, uint maxSide)
    {
        var longer = Math.Max(width, height);
        return longer <= maxSide || longer == 0 ? 1 : (double)maxSide / longer;
    }

    /// <summary>The picture as a JPEG at most <paramref name="maxSide"/> pixels on its longer side, for the describer.</summary>
    public static byte[] Jpeg(BitmapSource image, uint maxSide)
    {
        var (pixels, width, height) = OpaquePixels(image, FitScale(image.PixelWidth, image.PixelHeight, maxSide));
        var opaque = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(opaque));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

/// <summary>A picture couldn't be had; the message says why, for the person.</summary>
public sealed class PictureException(string message) : Exception(message);
