using QuickDescribe.Services;
using Windows.Media.Ocr;

namespace QuickDescribe.Tests;

[Collection("Text recognition")]
public class TextRecognitionTests
{
    public TextRecognitionTests() => TextRecognition.ForgetUnavailable();

    [Fact]
    public async Task AScreenSizedPicture_GoesToTheRecognizerWhole()
    {
        var helpers = new FakeHelpers();
        using var ai = helpers.Client();
        var picture = Drawn.Solid(3840, 2400, 255, 255, 255);
        await TextRecognition.ReadLinesAsync(picture, ai, TestContext.Current.CancellationToken);
        var png = Convert.FromBase64String((string)helpers.Started[0].Requests[0]["image"]!);
        var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(new MemoryStream(png),
            System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(3840, frame.PixelWidth); // under 4096: not shrunk, however many pixels
    }

    [Fact]
    public async Task WhenWindowsAIsRecognizerFails_TheBuiltInOcrReads()
    {
        NeedsOcr();
        var helpers = new FakeHelpers { Setup = h => h.Answer = r => FakeHelper.Fail(r, "internal_error") };
        using var ai = helpers.Client();
        var lines = await TextRecognition.ReadLinesAsync(Drawn.Text(700, 120, 28, "Save Cancel Help"), ai, TestContext.Current.CancellationToken);
        Assert.Equal(["Save Cancel Help"], lines);
    }

    [Fact]
    public async Task WhenTheRecognizerIsUnavailable_ItIsntAskedAgainForAWhile()
    {
        NeedsOcr();
        var helpers = new FakeHelpers { Setup = h => h.Answer = r => FakeHelper.Fail(r, "not_supported") };
        using var ai = helpers.Client();
        var picture = Drawn.Text(700, 120, 28, "Save Cancel Help");
        await TextRecognition.ReadLinesAsync(picture, ai, TestContext.Current.CancellationToken);
        await TextRecognition.ReadLinesAsync(picture, ai, TestContext.Current.CancellationToken);
        Assert.Single(helpers.Started[0].Requests);
    }

    [Theory]
    [InlineData(800, 300, 10000u, 2.0)]   // small, as a focused item: doubled
    [InlineData(1920, 1080, 10000u, 1.0)] // a screen: as it is
    [InlineData(6000, 1000, 10000u, 1.0)]
    [InlineData(1000, 500, 1200u, 1.2)]   // doubled only as far as OCR's limit
    [InlineData(20000, 100, 10000u, 0.5)] // cut to the limit
    public void ScaleFor_EnlargesSmallPictures_WithinTheLimit(int width, int height, uint max, double expected) =>
        Assert.Equal(expected, TextRecognition.ScaleFor(width, height, max), 3);

    private static void NeedsOcr()
    {
        if (OcrEngine.TryCreateFromUserProfileLanguages() is null) Assert.Skip("No OCR language on this PC.");
    }

    [Fact]
    public async Task ReadsTheTextInAPicture()
    {
        NeedsOcr();
        var picture = Drawn.Text(700, 200, 28, "Quick Describe reads text", "Save Cancel Help");
        var lines = await TextRecognition.ReadWithBuiltInOcrAsync(picture);
        Assert.Equal(["Quick Describe reads text", "Save Cancel Help"], lines);
    }

    [Fact]
    public async Task ReadsSmallScreenSizedText()
    {
        NeedsOcr();
        // 12 pixels is Segoe UI at 9 points, the size of much of Windows' own text at 100% scaling.
        var picture = Drawn.Text(400, 80, 12, "Open the Settings menu");
        var lines = await TextRecognition.ReadWithBuiltInOcrAsync(picture);
        Assert.Equal(["Open the Settings menu"], lines);
    }

    /// <summary>Windows AI's recognizer, where the helper is installed and the PC has it; skipped elsewhere.</summary>
    [Fact]
    public async Task WindowsAIsRecognizer_ReadsText()
    {
        var details = await WindowsAI.DetailsAsync(TestContext.Current.CancellationToken);
        if (!details.Components.TryGetValue("textRecognition", out var state) || state != "Ready") Assert.Skip("Windows AI's text recognizer isn't ready here.");
        using var ai = new WindowsAI();
        // The recognizer itself, not ReadLinesAsync, which would fall back to the built-in OCR if it failed.
        var picture = Drawn.Text(700, 200, 28, "Quick Describe reads text", "42 Harbor Road");
        var lines = await ai.ReadTextAsync(Pictures.Png(picture, TextRecognition.MaxAISide), TestContext.Current.CancellationToken);
        Assert.Equal(["Quick Describe reads text", "42 Harbor Road"], lines);
    }

    [Fact]
    public async Task WithoutTheHelper_TheBuiltInOcrReads()
    {
        NeedsOcr();
        var lines = await TextRecognition.ReadLinesAsync(Drawn.Text(700, 120, 28, "Save Cancel Help"), ai: null, TestContext.Current.CancellationToken);
        Assert.Equal(["Save Cancel Help"], lines);
    }

    [Fact]
    public async Task APictureWithoutText_HasNoLines()
    {
        NeedsOcr();
        Assert.Empty(await TextRecognition.ReadWithBuiltInOcrAsync(Drawn.Solid(300, 200, 0, 0, 200)));
    }
}
