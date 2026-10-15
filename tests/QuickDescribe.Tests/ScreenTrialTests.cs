using QuickDescribe.Services;

namespace QuickDescribe.Tests;

/// <summary>Takes and describes this PC's real screen, active window and focused item, without opening a window. Only
/// when QD_SCREEN_TRIAL=1: it depends on what's on the screen, so it's a trial to run by hand, not a test for CI.
/// Prints what happened (outcome, sizes, timings), not what was on the screen.</summary>
public class ScreenTrialTests
{
    [Fact]
    public async Task DescribesTheRealScreen()
    {
        if (Environment.GetEnvironmentVariable("QD_SCREEN_TRIAL") != "1") Assert.Skip("Set QD_SCREEN_TRIAL=1 to run.");
        var cancel = TestContext.Current.CancellationToken;
        using var ai = new WindowsAI();
        var foreground = Native.GetForegroundWindow();
        var pictures = new List<Picture>
        {
            Drawn.OnSta(() => ScreenCapture.Screen(foreground)),
            Drawn.OnSta(() => ScreenCapture.Window(foreground).Picture!),
            Drawn.OnSta(() => ScreenCapture.FocusAsync(foreground).GetAwaiter().GetResult().Picture!),
        };
        foreach (var picture in pictures)
        {
            var started = DateTime.UtcNow;
            var result = await Describing.RunAsync(picture.Image, "accessible", ai, cancel);
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"{picture.Source.Split(',')[0]}: {picture.Image.PixelWidth}x{picture.Image.PixelHeight}, " +
                $"description {(result.Description is null ? result.DescriptionCode : $"{result.Description.Length} chars")}, " +
                $"{result.Lines?.Count.ToString() ?? result.TextProblem} lines, {(DateTime.UtcNow - started).TotalSeconds:0.0} s");
            Assert.True(result.Description is not null || result.DescriptionCode is not null);
        }
    }
}

