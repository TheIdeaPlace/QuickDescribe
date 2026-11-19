using QuickDescribe.Services;
using Windows.Devices.Enumeration;

namespace QuickDescribe.Tests;

/// <summary>Choosing the camera, and waiting for it to settle. No test turns a camera on except the trial below.</summary>
public class CameraTests
{
    private static Camera.Device Device(string name, Panel? panel) => new(name, name, panel);

    [Fact]
    public void TheCameraOnTheBack_IsChosenFirst_ThenTheOneFacingYou_ThenOneWindowsCantPlace()
    {
        var front = Device("Front", Panel.Front);
        var back = Device("Back", Panel.Back);
        var unplaced = Device("OBS Virtual Camera", null);
        Assert.Equal(back, Camera.Choose([unplaced, front, back]));
        // Virtual cameras are among those Windows can't place: the PC's own camera comes first.
        Assert.Equal(front, Camera.Choose([unplaced, front]));
        Assert.Equal(unplaced, Camera.Choose([unplaced, Device("Unknown", Panel.Unknown)]));
        Assert.Null(Camera.Choose([]));
    }

    [Fact]
    public void ThePicture_IsTaken_WhenTheBrightnessStopsChanging_AfterTheLeastWarmUp()
    {
        var soon = Camera.MinWarmUp - TimeSpan.FromMilliseconds(100);
        var later = Camera.MinWarmUp + TimeSpan.FromMilliseconds(100);
        Assert.False(Camera.Settled(soon, 0.5, 0.5));    // steady, but too soon: exposure is still adjusting
        Assert.False(Camera.Settled(later, null, 0.5));  // the first frame: nothing to compare with
        Assert.False(Camera.Settled(later, 0.2, 0.4));   // still brightening
        Assert.True(Camera.Settled(later, 0.402, 0.405));
        Assert.True(Camera.Settled(Camera.MaxWarmUp, 0.2, 0.6)); // a scene that keeps changing: taken anyway
    }

    [Fact]
    public void Brightness_GoesFromBlackToWhite()
    {
        const int pixels = 1000;
        Assert.Equal(0, Camera.Brightness(new byte[pixels * 4], pixels));
        Assert.Equal(1, Camera.Brightness(Enumerable.Repeat((byte)255, pixels * 4).ToArray(), pixels), 3);
        // Green counts most, as it does to the eye.
        var green = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++) green[i * 4 + 1] = 255;
        Assert.Equal(0.587, Camera.Brightness(green, pixels), 3);
    }
}

/// <summary>Takes a real picture with this PC's camera and describes it. Only when QD_CAMERA_TRIAL=1: it turns the
/// camera on, so it's run by hand. Prints what happened (camera, size, timings, outcome), not what was in the picture.</summary>
public class CameraTrialTests
{
    [Fact]
    public async Task TakesAndDescribesAPicture()
    {
        if (Environment.GetEnvironmentVariable("QD_CAMERA_TRIAL") != "1") Assert.Skip("Set QD_CAMERA_TRIAL=1 to run.");
        var cancel = TestContext.Current.CancellationToken;
        var started = DateTime.UtcNow;
        // The camera gives up after 30 seconds, rather than holding the run.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        var picture = await Camera.TakeAsync(limit.Token);
        var taken = DateTime.UtcNow;
        TestContext.Current.TestOutputHelper!.WriteLine($"{picture.Source}: taken in {(taken - started).TotalSeconds:0.0} s");
        using var ai = new WindowsAI();
        var result = await Describing.RunAsync(picture.Image, "accessible", ai, cancel);
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{picture.Source}: {picture.Image.PixelWidth}x{picture.Image.PixelHeight} in {(taken - started).TotalSeconds:0.0} s, " +
            $"description {(result.Description is null ? result.DescriptionCode : $"{result.Description.Length} chars")}, " +
            $"{result.Lines?.Count.ToString() ?? result.TextProblem} lines, {(DateTime.UtcNow - taken).TotalSeconds:0.0} s");
        Assert.True(result.Description is not null || result.DescriptionCode is not null);
    }
}
