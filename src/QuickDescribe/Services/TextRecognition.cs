using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace QuickDescribe.Services;

/// <summary>
/// Reads the text in a picture. On a Copilot+ PC, with Windows AI's text recognizer, through the helper: it reads screens
/// far better (it keeps the "4" of "42" beside a text box's border, where the older OCR drops it). Everywhere else, and
/// whenever that fails, with Windows' built-in OCR, which every Windows 10 and 11 PC has in the languages installed for
/// the person, and which needs no package identity or NPU.
/// </summary>
public static class TextRecognition
{
    /// <summary>Below this on its longer side, a picture is doubled for the built-in OCR: text on screen is small, and it
    /// misses letters under about 10 pixels high.</summary>
    internal const int EnlargeBelow = 1600;

    /// <summary>The largest side sent to Windows AI's recognizer: the helper's own limit.</summary>
    internal const uint MaxAISide = 4096;

    public const string NoLanguageMessage =
        "Windows has no text recognition for your languages. Add a language with its Optical character recognition feature in Settings, Time & language.";

    /// <summary>Set once Windows AI's recognizer has said it can't be used on this PC, so it isn't asked again.</summary>
    private static volatile bool _noAIRecognizer;

    /// <summary>The helper's codes that mean the recognizer won't work on this PC at all, as opposed to this picture.</summary>
    private static readonly string[] Unavailable = ["not_installed", "not_supported", "disabled_by_user", "not_ready"];

    /// <summary>The lines of text found, top to bottom. Call on the UI thread and await.</summary>
    public static async Task<IReadOnlyList<string>> ReadLinesAsync(BitmapSource image, WindowsAI? ai, CancellationToken cancel = default)
    {
        if (ai is not null && !_noAIRecognizer && WindowsAI.IsInstalled)
        {
            try
            {
                var png = await Task.Run(() => Pictures.Png(image, MaxAISide), cancel);
                return await ai.ReadTextAsync(png, cancel);
            }
            catch (DescriptionException ex)
            {
                if (Unavailable.Contains(ex.Code)) _noAIRecognizer = true;
                // Otherwise this picture only: the built-in OCR tries it.
            }
        }
        return await ReadWithBuiltInOcrAsync(image);
    }

    /// <summary>Windows' built-in OCR (Windows.Media.Ocr).</summary>
    public static async Task<IReadOnlyList<string>> ReadWithBuiltInOcrAsync(BitmapSource image)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new PictureException(NoLanguageMessage);
        var scale = ScaleFor(image.PixelWidth, image.PixelHeight, OcrEngine.MaxImageDimension);
        var (pixels, width, height) = Pictures.OpaquePixels(image, scale);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return result.Lines.Select(l => l.Text.Trim()).Where(t => t.Length > 0).ToList();
    }

    /// <summary>The scale to read a picture at with the built-in OCR: doubled when small, cut to fit its largest size when large.</summary>
    internal static double ScaleFor(int width, int height, uint maxDimension)
    {
        var longer = Math.Max(width, height);
        if (longer == 0) return 1;
        var scale = longer < EnlargeBelow ? 2.0 : 1.0;
        return Math.Min(scale, (double)maxDimension / longer);
    }
}
