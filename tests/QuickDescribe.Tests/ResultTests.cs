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
    public void APCThatCantDescribe_GivesTheTextAlone()
    {
        var result = new Result(null, Result.TextOnlyCode, "Needs a Copilot+ PC.", ["OPEN", "Mon-Fri"], null);
        Assert.Equal($"Text in the picture:{Environment.NewLine}OPEN{Environment.NewLine}Mon-Fri", result.BoxText());
        Assert.Equal("2 lines of text follow.", result.Announcement());
        Assert.Equal("No text found.", new Result(null, Result.TextOnlyCode, null, [], null).Announcement());
        Assert.Equal("Text couldn't be read: No OCR here.", new Result(null, Result.TextOnlyCode, null, null, "No OCR here.").BoxText());
    }

    [Fact]
    public void APCThatCantDescribe_SaysSoWhenAsked()
    {
        var result = new Result(null, Result.TextOnlyCode, "Needs a Copilot+ PC.", ["OPEN"], null);
        Assert.Equal($"{Result.TextOnlyNote}{Gap}Text in the picture:{Environment.NewLine}OPEN", result.BoxText(sayTextOnly: true));
        Assert.Equal($"{Result.TextOnlyNote} 1 line of text follows.", result.Announcement(sayTextOnly: true));
    }

    [Fact]
    public void TheTextOnlyNote_ComesWithNoTextOrUnreadableText_Too()
    {
        var none = new Result(null, Result.TextOnlyCode, null, [], null);
        Assert.Equal($"{Result.TextOnlyNote}{Gap}No text found.", none.BoxText(sayTextOnly: true));
        Assert.Equal($"{Result.TextOnlyNote} No text found.", none.Announcement(sayTextOnly: true));
        var unreadable = new Result(null, Result.TextOnlyCode, null, null, "No OCR here.");
        Assert.Equal($"{Result.TextOnlyNote}{Gap}Text couldn't be read: No OCR here.", unreadable.BoxText(sayTextOnly: true));
        Assert.Equal($"{Result.TextOnlyNote} Text couldn't be read.", unreadable.Announcement(sayTextOnly: true));
    }

    [Theory]
    [InlineData("NotSupportedOnCurrentSystem", true)]
    [InlineData("DisabledByUser", false)]
    [InlineData("NotReady", false)]
    [InlineData("Error", false)]
    [InlineData("Ready", false)]
    public void OnlyNotSupported_IsTextOnly(string state, bool textOnly) => Assert.Equal(textOnly, WindowsAI.IsTextOnly(state));

    [Fact]
    public void TheTextOnlyNote_IsSaidUntilItsBeenHeard_AndOnlyWithTextOnlyResults()
    {
        var textOnly = new Result(null, Result.TextOnlyCode, null, ["OPEN"], null);
        Assert.True(textOnly.SaysTextOnly(alreadySaid: false));
        Assert.False(textOnly.SaysTextOnly(alreadySaid: true));
        Assert.False(new Result("A red mug.", null, null, [], null).SaysTextOnly(alreadySaid: false));
        Assert.False(new Result(null, "unavailable", "Turned off.", [], null).SaysTextOnly(alreadySaid: false));
    }

    [Fact]
    public void SayingTextOnly_IsIgnoredWhenThereIsADescription()
    {
        var result = new Result("A red mug.", null, null, [], null);
        Assert.Equal(result.Announcement(), result.Announcement(sayTextOnly: true));
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
