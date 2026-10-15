using System.Text.Json.Nodes;
using QuickDescribe.Services;

namespace QuickDescribe.Tests;

public class WindowsAITests
{
    [Fact]
    public void ASuccess_GivesTheTrimmedDescription()
    {
        Assert.Equal("A red mug.", WindowsAI.ParseResponse("""{"id":3,"ok":true,"kind":"accessible","text":"  A red mug.\n","seconds":1.2}""", 3));
    }

    [Fact]
    public void AFailure_CarriesTheHelpersCodeAndMessage()
    {
        var ex = Assert.Throws<DescriptionException>(() =>
            WindowsAI.ParseResponse("""{"id":3,"ok":false,"code":"too_much_text","message":"Too much text."}""", 3));
        Assert.Equal("too_much_text", ex.Code);
        Assert.Equal("Too much text.", ex.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"id":"three","ok":true}""")]
    [InlineData("""{"id":3,"ok":"yes"}""")]
    public void AnUnreadableAnswer_IsABadResponse(string line)
    {
        Assert.Equal("bad_response", Assert.Throws<DescriptionException>(() => WindowsAI.ParseResponse(line, 3)).Code);
    }

    [Fact]
    public void AnAnswerToAnotherRequest_IsABadResponse_ButIdZeroIsAccepted()
    {
        Assert.Equal("bad_response", Assert.Throws<DescriptionException>(() =>
            WindowsAI.ParseResponse("""{"id":4,"ok":true,"text":"x"}""", 3)).Code);
        // The helper answers 0 when it couldn't read the request's id.
        Assert.Equal("too_large", Assert.Throws<DescriptionException>(() =>
            WindowsAI.ParseResponse("""{"id":0,"ok":false,"code":"too_large","message":"m"}""", 3)).Code);
    }

    [Fact]
    public void TheRequest_IsOneLineOfJson_WithTheJpegInBase64()
    {
        var line = WindowsAI.BuildRequest(5, "brief", [0xFF, 0xD8, 0x01]);
        Assert.DoesNotContain('\n', line);
        var o = JsonNode.Parse(line)!;
        Assert.Equal(5, (int)o["id"]!);
        Assert.Equal("brief", (string)o["kind"]!);
        Assert.Equal("image/jpeg", (string)o["mime"]!);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0x01 }, Convert.FromBase64String((string)o["image"]!));
    }

    [Fact]
    public void Readiness_IsRead_WithItsComponents()
    {
        var r = WindowsAI.ParseReadiness("""{"protocol":1,"version":"0.1.0","identity":true,"state":"Ready","components":{"languageModel":"NotSupportedOnCurrentSystem"}}""");
        Assert.True(r.Usable);
        Assert.Equal("0.1.0", r.HelperVersion);
        Assert.Equal("NotSupportedOnCurrentSystem", r.Components["languageModel"]);
    }

    [Theory]
    [InlineData("""{"identity":true,"state":"NotReady"}""", true)]
    [InlineData("""{"identity":false,"state":"NoIdentity","message":"Not installed."}""", false)]
    [InlineData("""{"identity":true,"state":"NotSupportedOnCurrentSystem"}""", false)]
    [InlineData("""{"identity":true,"state":"DisabledByUser"}""", false)]
    [InlineData("garbage", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Readiness_IsUsable_OnlyWhenReadyOrPreparable(string? line, bool usable)
    {
        var r = WindowsAI.ParseReadiness(line);
        Assert.Equal(usable, r.Usable);
        if (!usable) Assert.False(string.IsNullOrWhiteSpace(r.Message));
    }

    [Fact]
    public async Task AnInternalError_IsTriedAgainSmaller_ThenGivenUp()
    {
        var sizes = new List<uint>();
        var ex = await Assert.ThrowsAsync<DescriptionException>(() => WindowsAI.WithRetriesAsync<string>(
            size => { sizes.Add(size); return Task.FromResult(new byte[1]); },
            _ => throw new DescriptionException("internal_error", "boom")));
        Assert.Equal("internal_error", ex.Code);
        Assert.Equal([2048u, 1024u, 512u], sizes);
    }

    [Fact]
    public async Task OtherFailures_AreNotTriedAgain()
    {
        var tries = 0;
        await Assert.ThrowsAsync<DescriptionException>(() => WindowsAI.WithRetriesAsync<string>(
            _ => { tries++; return Task.FromResult(new byte[1]); },
            _ => throw new DescriptionException("too_much_text", "text")));
        Assert.Equal(1, tries);
    }

    [Fact]
    public async Task ASmallerSizeThatWorks_GivesItsDescription()
    {
        var result = await WindowsAI.WithRetriesAsync(
            size => Task.FromResult(new byte[size / 512]),
            jpeg => jpeg.Length == 2 ? Task.FromResult("described at 1024") : throw new DescriptionException("internal_error", "boom"));
        Assert.Equal("described at 1024", result);
    }

    /// <summary>The whole way through Windows' model, on a PC that has it: a Copilot+ PC with the helper registered
    /// (scripts\build-ai-helper.ps1 -Register) or Quick Describe installed. Skipped everywhere else, CI included.</summary>
    [Fact]
    public async Task DescribesAPicture_WithWindowsAI()
    {
        var readiness = await WindowsAI.CheckAsync(TestContext.Current.CancellationToken);
        if (readiness.State != "Ready") Assert.Skip($"Windows AI isn't ready here: {readiness.State}.");
        using var ai = new WindowsAI();
        var picture = Drawn.Solid(640, 480, 0, 0, 220);
        var result = await Describing.RunAsync(picture, "brief", ai, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(result.Description), result.DescriptionProblem);
        Assert.Empty(result.Lines!);
    }
}

public class SettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("QuickDescribeSettings").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void TheKind_IsKept()
    {
        var path = Path.Combine(_folder, "sub", "settings.json");
        new Settings { Kind = "detailed" }.Save(path);
        Assert.Equal("detailed", Settings.Load(path).Kind);
    }

    [Fact]
    public void NoFile_OrABadOne_GivesTheDefaults()
    {
        Assert.Equal("accessible", Settings.Load(Path.Combine(_folder, "none.json")).Kind);
        var bad = Path.Combine(_folder, "bad.json");
        File.WriteAllText(bad, "{ not json");
        Assert.Equal("accessible", Settings.Load(bad).Kind);
        File.WriteAllText(bad, """{"Kind":"poetic"}""");
        Assert.Equal("accessible", Settings.Load(bad).Kind);
    }
}

public class HotKeysTests
{
    [Fact]
    public void EachTarget_HasItsOwnLetter()
    {
        Assert.Equal("Ctrl+Alt+Shift+S", HotKeys.GestureFor(Target.Screen));
        Assert.Equal("Ctrl+Alt+Shift+W", HotKeys.GestureFor(Target.Window));
        Assert.Equal("Ctrl+Alt+Shift+F", HotKeys.GestureFor(Target.Focus));
        Assert.Equal("Ctrl+Alt+Shift+V", HotKeys.GestureFor(Target.Clipboard));
        Assert.Equal(HotKeys.All.Length, HotKeys.All.Select(h => h.Key).Distinct().Count());
        Assert.Equal(Enum.GetValues<Target>().Length, HotKeys.All.Length);
    }
}
