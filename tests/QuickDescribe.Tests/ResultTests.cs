using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class ResultTests
{
    private static readonly string Gap = Environment.NewLine + Environment.NewLine;

    [Fact]
    public void ADescriptionAndText_AreShownInThatOrder_AndOnlyTheDescriptionIsSpoken()
    {
        var result = new Result("A red mug on a table.", null, null, ["OPEN", "Mon-Fri"], null);
        Assert.Equal($"A red mug on a table.{Gap}Text in the picture:{Environment.NewLine}OPEN{Environment.NewLine}Mon-Fri", result.BoxText());
        Assert.Equal("A red mug on a table. 2 lines of text follow.", result.Announcement());
    }

    [Fact]
    public void OneLine_IsSaidInTheSingular()
    {
        Assert.EndsWith("1 line of text follows.", new Result("A sign.", null, null, ["OPEN"], null).Announcement());
    }

    [Fact]
    public void TooMuchText_SaysSo_AndTheTextStillComes()
    {
        var result = new Result(null, "too_much_text", "This picture has too much text for Windows to describe. Try OCR instead.", ["File", "Edit"], null);
        Assert.StartsWith("No description: Windows' AI doesn't describe pictures with this much text.", result.BoxText());
        Assert.Contains("File", result.BoxText());
        Assert.DoesNotContain("Try OCR", result.BoxText());
        Assert.Equal("No description: Windows' AI doesn't describe pictures with this much text. 2 lines of text follow.", result.Announcement());
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("not_installed")]
    [InlineData("not_supported")]
    [InlineData("disabled_by_user")]
    public void NoWindowsAI_PointsToCheckWindowsAI(string code)
    {
        var result = new Result(null, code, "long reason", [], null);
        Assert.Equal($"No description: Windows' AI isn't available on this PC. Help, Check Windows AI says why.{Gap}No text found.", result.BoxText());
    }

    [Fact]
    public void OtherFailures_SayTheHelpersMessage()
    {
        var result = new Result(null, "content_filtered", "Windows' content filter declined to describe this picture.", null, "No OCR here.");
        Assert.Equal($"No description: Windows' content filter declined to describe this picture.{Gap}Text couldn't be read: No OCR here.", result.BoxText());
        Assert.EndsWith("Text couldn't be read.", result.Announcement());
    }

    [Fact]
    public void ABlankDescription_CountsAsNone()
    {
        Assert.StartsWith("No description: Windows couldn't describe this picture.", new Result("  ", null, null, [], null).BoxText());
    }
}
