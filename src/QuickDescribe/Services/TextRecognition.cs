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

    /// <summary>The largest side sent to Windows AI's recognizer: the helper's own limit. A picture whose PNG is too big at
    /// that (a camera photo; a screen's PNG is small at any size) goes again at <see cref="PhotoSide"/>.</summary>
    internal const uint MaxAISide = 4096;
    internal const uint PhotoSide = 2560;

    /// <summary>The largest PNG sent: the helper takes 20 MB, less room for the rest of the request.</summary>
    internal const int MaxPngBytes = 19 * 1024 * 1024;

    public const string NoLanguageMessage =
        "Windows has no text recognition for your languages. Add a language with its Optical character recognition feature in Settings, Time & language.";

    /// <summary>When Windows AI's recognizer last said it can't be used on this PC; it's asked again after
    /// <see cref="RetryAfter"/>, since it may have been getting ready or turned off in Settings.</summary>
    private static DateTime? _aiUnavailableAt;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    /// <summary>The helper's codes that mean the recognizer won't work on this PC for now, as opposed to this picture.</summary>
    private static readonly string[] Unavailable = ["not_installed", "not_supported", "disabled_by_user", "not_ready"];

    /// <summary>The lines of text found, top to bottom: from Windows AI's recognizer when it can, otherwise, or when it fails
    /// in any way, from the built-in OCR. Call on the UI thread and await.</summary>
    public static async Task<IReadOnlyList<string>> ReadLinesAsync(BitmapSource image, WindowsAI? ai, CancellationToken cancel = default)
    {
        if (ai is not null && (_aiUnavailableAt is not { } at || DateTime.UtcNow - at > RetryAfter))
        {
            try
            {
                var png = await Task.Run(() => Pictures.Png(image, MaxAISide), cancel);
                if (png.Length > MaxPngBytes && Math.Max(image.PixelWidth, image.PixelHeight) > PhotoSide)
                    png = await Task.Run(() => Pictures.Png(image, PhotoSide), cancel);
                if (png.Length <= MaxPngBytes)
                {
                    var lines = await ai.ReadTextAsync(png, cancel);
                    _aiUnavailableAt = null;
                    return lines;
                }
            }
            catch (DescriptionException ex) when (Unavailable.Contains(ex.Code))
            {
                _aiUnavailableAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // This picture only (too much for the helper, say): the built-in OCR tries it.
            }
        }
        cancel.ThrowIfCancellationRequested();
        return await ReadWithBuiltInOcrAsync(image);
    }

    /// <summary>For tests: forget that Windows AI's recognizer was unavailable.</summary>
    internal static void ForgetUnavailable() => _aiUnavailableAt = null;

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
