using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace QuickDescribe.Services;

/// <summary>Reads the text in a picture with Windows' built-in OCR, which every Windows 10 and 11 PC has (in the
/// languages installed for the person), and which needs no package identity or NPU.</summary>
public static class TextRecognition
{
    /// <summary>Below this on its longer side, a picture is doubled before reading: text on screen is small, and
    /// Windows' OCR misses letters under about 10 pixels high.</summary>
    internal const int EnlargeBelow = 1600;

    public const string NoLanguageMessage =
        "Windows has no text recognition for your languages. Add a language with its Optical character recognition feature in Settings, Time & language.";

    /// <summary>The lines of text found, top to bottom. Call on the UI thread (it reads the picture's pixels there) and
    /// await: the reading itself runs elsewhere.</summary>
    public static async Task<IReadOnlyList<string>> ReadLinesAsync(BitmapSource image)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new PictureException(NoLanguageMessage);
        var scale = ScaleFor(image.PixelWidth, image.PixelHeight, OcrEngine.MaxImageDimension);
        var (pixels, width, height) = Pictures.OpaquePixels(image, scale);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return result.Lines.Select(l => l.Text.Trim()).Where(t => t.Length > 0).ToList();
    }

    /// <summary>The scale to read a picture at: doubled when small, cut to fit OCR's largest size when large.</summary>
    internal static double ScaleFor(int width, int height, uint maxDimension)
    {
        var longer = Math.Max(width, height);
        if (longer == 0) return 1;
        var scale = longer < EnlargeBelow ? 2.0 : 1.0;
        return Math.Min(scale, (double)maxDimension / longer);
    }
}
