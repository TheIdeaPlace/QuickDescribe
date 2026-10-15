using QuickDescribe.Services;
using Windows.Media.Ocr;

namespace QuickDescribe.Tests;

public class TextRecognitionTests
{
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
        var lines = await TextRecognition.ReadLinesAsync(picture);
        Assert.Equal(["Quick Describe reads text", "Save Cancel Help"], lines);
    }

    [Fact]
    public async Task ReadsSmallScreenSizedText()
    {
        NeedsOcr();
        // 12 pixels is Segoe UI at 9 points, the size of much of Windows' own text at 100% scaling.
        var picture = Drawn.Text(400, 80, 12, "Open the Settings menu");
        var lines = await TextRecognition.ReadLinesAsync(picture);
        Assert.Equal(["Open the Settings menu"], lines);
    }

    [Fact]
    public async Task APictureWithoutText_HasNoLines()
    {
        NeedsOcr();
        Assert.Empty(await TextRecognition.ReadLinesAsync(Drawn.Solid(300, 200, 0, 0, 200)));
    }
}
