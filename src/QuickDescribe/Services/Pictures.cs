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

    /// <summary>The picture as a PNG at most <paramref name="maxSide"/> pixels on its longer side, for reading its text.</summary>
    public static byte[] Png(BitmapSource image, uint maxSide)
    {
        var (pixels, width, height) = OpaquePixels(image, FitScale(image.PixelWidth, image.PixelHeight, maxSide));
        var opaque = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(opaque));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    // ---- Saving a screenshot ----

    /// <summary>The types Save Screenshot offers, PNG first: it keeps a screen's text sharp.</summary>
    public const string SaveFilter = "PNG picture|*.png|JPEG picture|*.jpg;*.jpeg";

    /// <summary>The longest a suggested name's source part may be, so a long window title doesn't make an unwieldy name.</summary>
    private const int NameSourceLength = 60;

    /// <summary>The name Save Screenshot suggests: what was taken and when, such as "Active window, Notepad 2026-10-30
    /// 14.05.12.png", with the characters a file name can't have left out.</summary>
    public static string ScreenshotName(string source, DateTime taken)
    {
        // The note is for hearing, not for a file's name: it's the active window's picture.
        if (source.EndsWith(ScreenCapture.NoPlaceNote, StringComparison.Ordinal)) source = source[..^ScreenCapture.NoPlaceNote.Length];
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(source.Select(c => invalid.Contains(c) ? ' ' : c).ToArray());
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > NameSourceLength)
        {
            // Not between the halves of an emoji, which would leave half of one in the name.
            var cut = char.IsHighSurrogate(name[NameSourceLength - 1]) ? NameSourceLength - 1 : NameSourceLength;
            name = name[..cut].TrimEnd();
        }
        // Windows drops a trailing dot from a name, which would lose the space before the date.
        name = name.TrimEnd('.', ' ');
        if (name.Length == 0) name = "Screenshot";
        return $"{name} {taken:yyyy-MM-dd HH.mm.ss}.png";
    }

    /// <summary>A picture made ready for the clipboard: the slow part of copying it, so it can run off the UI thread. The
    /// bitmap is 24-bit: a screen's 32-bit pixels have an unset fourth byte, which apps that read it as transparency
    /// would paste as a blank picture.</summary>
    public static (BitmapSource Bitmap, MemoryStream Png) ForClipboard(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        var stride = (converted.PixelWidth * 3 + 3) / 4 * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        var bitmap = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, image.DpiX, image.DpiY, PixelFormats.Bgr24, null, pixels, stride);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var png = new MemoryStream();
        encoder.Save(png);
        png.Position = 0;
        return (bitmap, png);
    }

    /// <summary>What goes on the clipboard: the bitmap (what most apps paste; Windows offers it as a DIB too) and the PNG
    /// (what browsers and Office prefer, and what Describe Clipboard reads first). Only those, without WPF's own
    /// formats, so copying doesn't make more copies of a whole screen than apps use.</summary>
    public static DataObject ClipboardData(BitmapSource bitmap, MemoryStream png)
    {
        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, bitmap, false);
        data.SetData("PNG", png, false);
        return data;
    }

    /// <summary>Saves a picture as the file's extension says: JPEG for .jpg and .jpeg, PNG otherwise. Throws a
    /// <see cref="PictureException"/> with a readable message when it can't.</summary>
    public static void Save(BitmapSource image, string path)
    {
        BitmapEncoder encoder = Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg"
            ? new JpegBitmapEncoder { QualityLevel = 90 }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        // Written beside it first, so a failure part way doesn't leave a broken picture, or spoil one being replaced.
        var partial = path + ".partial";
        try
        {
            using (var stream = File.Create(partial)) encoder.Save(stream);
            File.Move(partial, path, overwrite: true);
        }
        catch (Exception ex)
        {
            try { File.Delete(partial); }
            catch (Exception) { /* nothing more to do */ }
            if (ex is not (IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)) throw;
            throw new PictureException($"{Path.GetFileName(path)} couldn't be saved: {SaveProblem(ex, partial, path)}");
        }
    }

    private const int DiskFull = unchecked((int)0x80070070);

    /// <summary>Why a save failed, in words that name the file the person chose, not the one written beside it first.</summary>
    internal static string SaveProblem(Exception ex, string partial, string path) => ex switch
    {
        UnauthorizedAccessException => "Windows doesn't allow saving in that folder, or the file is read-only.",
        DirectoryNotFoundException => "the folder isn't there any more.",
        IOException { HResult: DiskFull } => "the drive is full.",
        _ => ex.Message.Replace(partial, path, StringComparison.OrdinalIgnoreCase).Trim(),
    };
}

/// <summary>A picture couldn't be had; the message says why, for the person.</summary>
public sealed class PictureException(string message) : Exception(message);
