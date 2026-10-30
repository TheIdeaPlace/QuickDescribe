using System.Xml.Linq;
using QuickDescribe.Services;
using QuickDescribe.Views;
using Windows.ApplicationModel;

namespace QuickDescribe.Tests;

/// <summary>Settings' "Start Quick Describe when I sign in to Windows" and "Keep running in the notification area".</summary>
public sealed class StartAndCloseTests
{
    [Theory]
    [InlineData(StartupTaskState.Enabled, SignInStart.On)]
    [InlineData(StartupTaskState.Disabled, SignInStart.Off)]
    [InlineData(StartupTaskState.DisabledByUser, SignInStart.OffInWindows)]
    [InlineData(StartupTaskState.DisabledByPolicy, SignInStart.OffByPolicy)]
    [InlineData(StartupTaskState.EnabledByPolicy, SignInStart.OnByPolicy)]
    public void WindowsStartupTaskStates_AreWhatSettingsShows(StartupTaskState state, SignInStart shown) =>
        Assert.Equal(shown, StartAtSignIn.From(state));

    [Fact]
    public void TheCheckBox_IsOn_OnlyWhenWindowsStartsIt()
    {
        Assert.True(StartAtSignIn.IsOn(SignInStart.On));
        Assert.True(StartAtSignIn.IsOn(SignInStart.OnByPolicy));
        Assert.False(StartAtSignIn.IsOn(SignInStart.Off));
        Assert.False(StartAtSignIn.IsOn(SignInStart.OffInWindows));
        Assert.False(StartAtSignIn.IsOn(SignInStart.OffByPolicy));
        Assert.False(StartAtSignIn.IsOn(SignInStart.Unavailable));
    }

    [Fact]
    public void AChoiceThatTook_SaysNothing()
    {
        Assert.Null(StartAtSignIn.Outcome(true, SignInStart.On));
        Assert.Null(StartAtSignIn.Outcome(true, SignInStart.OnByPolicy));
        Assert.Null(StartAtSignIn.Outcome(false, SignInStart.Off));
        Assert.Null(StartAtSignIn.Outcome(false, SignInStart.OffInWindows));
        Assert.Null(StartAtSignIn.Outcome(false, SignInStart.OffByPolicy));
    }

    [Fact]
    public void AChoiceWindowsDidntTake_IsSaid_WithWhereToChangeIt()
    {
        // Turned off in Task Manager or Windows Settings: only the person can turn it on, there.
        Assert.Equal("Quick Describe is turned off in Windows' startup apps, so it can't turn itself on. Turn it on in Windows Settings, under Apps, Startup.",
            StartAtSignIn.Outcome(true, SignInStart.OffInWindows));
        Assert.Contains("organization doesn't let", StartAtSignIn.Outcome(true, SignInStart.OffByPolicy));
        Assert.Contains("can't be turned off here", StartAtSignIn.Outcome(false, SignInStart.OnByPolicy));
        Assert.Contains("isn't installed", StartAtSignIn.Outcome(true, SignInStart.Unavailable));
        Assert.Contains("didn't turn on", StartAtSignIn.Outcome(true, SignInStart.Off));
        Assert.Contains("Apps, Startup", StartAtSignIn.Outcome(false, SignInStart.On));
    }

    [Fact]
    public async Task ADevelopmentCopy_HasNoStartupTask_AndWasntStartedAtSignIn()
    {
        // The tests run unpackaged.
        Assert.Equal(SignInStart.Unavailable, await new StartAtSignIn().GetAsync());
        Assert.Equal(SignInStart.Unavailable, await new StartAtSignIn().SetAsync(true));
        Assert.False(StartAtSignIn.StartedBySignIn());
    }

    [Fact]
    public void ThePackage_DeclaresTheStartupTask_OffUntilChosen_ForTheApp()
    {
        var manifest = XDocument.Load(Path.Combine(Repo(), "packaging", "AppxManifest.xml"));
        XNamespace desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
        var task = Assert.Single(manifest.Descendants(desktop + "StartupTask"));
        Assert.Equal(StartAtSignIn.TaskId, (string?)task.Attribute("TaskId"));
        Assert.Equal("false", (string?)task.Attribute("Enabled"));
        var extension = task.Parent!;
        Assert.Equal("windows.startupTask", (string?)extension.Attribute("Category"));
        Assert.Equal(@"App\QuickDescribe.exe", (string?)extension.Attribute("Executable"));
        Assert.Contains("desktop", ((string?)manifest.Root!.Attribute("IgnorableNamespaces"))!.Split(' '));
    }

    [Fact]
    public void KeepRunning_IsOffUnlessChosen_AndKept()
    {
        Assert.False(new Settings().KeepRunning);
        var path = Path.Combine(Path.GetTempPath(), $"qd-settings-{Guid.NewGuid():N}.json");
        try
        {
            new Settings { KeepRunning = true, KeepRunningSaid = true }.Save(path);
            var back = Settings.Load(path);
            Assert.True(back.KeepRunning);
            Assert.True(back.KeepRunningSaid);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void F1_SaysWhatAltF4Does()
    {
        Assert.EndsWith("Alt+F4 quits, and the hotkeys stop.", MainWindow.InAppKeys(false));
        Assert.EndsWith("Alt+F4 closes the window, and Quick Describe keeps running in the notification area with the hotkeys working. File, Exit quits.",
            MainWindow.InAppKeys(true));
    }

    [Fact]
    public void F1_SaysCtrlSSavesTheScreenshot()
    {
        Assert.Contains("Ctrl+S saves the screenshot", MainWindow.InAppKeys(false));
        Assert.Contains("Describe the screen", MainWindow.NoScreenshotText);
    }

    [Fact]
    public void TheFirstClose_SaysHowToQuit()
    {
        Assert.Contains("File, Exit", MainWindow.KeepRunningText);
        Assert.Contains("notification area", MainWindow.KeepRunningText);
    }

    [Theory]
    [InlineData(false, false, false)] // closing quits, as it always did
    [InlineData(true, false, true)]   // Keep running: closing hides
    [InlineData(true, true, false)]   // File, Exit, the icon's Exit, signing out or an update: quits anyway
    [InlineData(false, true, false)]
    public void Closing_HidesOnlyWithKeepRunning_AndNotWhenExiting(bool keepRunning, bool exiting, bool hides) =>
        Assert.Equal(hides, MainWindow.HidesOnClose(keepRunning, exiting));

    [Fact]
    public async Task Settings_LeavesTheStartupTaskAlone_WhenItsAlreadyAsChosen_OrCantBeChanged()
    {
        var on = new FakeStartup(SignInStart.On);
        Assert.Null(await StartAtSignIn.ApplyAsync(on, true));
        var off = new FakeStartup(SignInStart.Off);
        Assert.Null(await StartAtSignIn.ApplyAsync(off, false));
        var policy = new FakeStartup(SignInStart.OnByPolicy);
        Assert.Null(await StartAtSignIn.ApplyAsync(policy, true));
        var notInstalled = new FakeStartup(SignInStart.Unavailable);
        Assert.Null(await StartAtSignIn.ApplyAsync(notInstalled, true));
        Assert.Empty(on.Asked.Concat(off.Asked).Concat(policy.Asked).Concat(notInstalled.Asked));
        // Turned off in Windows and left unchecked: nothing to change.
        var offInWindows = new FakeStartup(SignInStart.OffInWindows);
        Assert.Null(await StartAtSignIn.ApplyAsync(offInWindows, false));
        Assert.Empty(offInWindows.Asked);
    }

    [Fact]
    public async Task Settings_TurnsTheStartupTaskOnAndOff_SayingNothingWhenWindowsAgrees()
    {
        var startup = new FakeStartup(SignInStart.Off);
        Assert.Null(await StartAtSignIn.ApplyAsync(startup, true));
        Assert.Null(await StartAtSignIn.ApplyAsync(startup, false));
        Assert.Equal([true, false], startup.Asked);
    }

    [Fact]
    public async Task WhenWindowsWontTurnItOn_SettingsSaysWhy_OfferingTheStartupAppsPage_OnlyWhereThePersonCanChangeIt()
    {
        var userTurnedOff = await StartAtSignIn.ApplyAsync(new FakeStartup(SignInStart.OffInWindows, after: SignInStart.OffInWindows), true);
        Assert.NotNull(userTurnedOff);
        Assert.True(userTurnedOff.OfferStartupApps);
        Assert.Contains("startup apps", userTurnedOff.Message);

        var organization = await StartAtSignIn.ApplyAsync(new FakeStartup(SignInStart.OffByPolicy, after: SignInStart.OffByPolicy), true);
        Assert.NotNull(organization);
        Assert.False(organization.OfferStartupApps);

        var couldntTurnOff = await StartAtSignIn.ApplyAsync(new FakeStartup(SignInStart.On, after: SignInStart.On), false);
        Assert.NotNull(couldntTurnOff);
        Assert.True(couldntTurnOff.OfferStartupApps);

        var organizationOn = await StartAtSignIn.ApplyAsync(new FakeStartup(SignInStart.OnByPolicy, after: SignInStart.OnByPolicy), false);
        Assert.NotNull(organizationOn);
        Assert.False(organizationOn.OfferStartupApps);
    }

    [Fact]
    public void TheCheckBox_SaysWhenItsStateIsntThePersonsToChangeThere()
    {
        Assert.Equal("turned off in Windows' startup apps", StartAtSignIn.StateNote(SignInStart.OffInWindows));
        Assert.Equal("turned off by your organization", StartAtSignIn.StateNote(SignInStart.OffByPolicy));
        Assert.Equal("turned on by your organization", StartAtSignIn.StateNote(SignInStart.OnByPolicy));
        Assert.Null(StartAtSignIn.StateNote(SignInStart.On));
        Assert.Null(StartAtSignIn.StateNote(SignInStart.Off));
        Assert.Null(StartAtSignIn.StateNote(SignInStart.Unavailable));
    }

    [Fact]
    public void TheNotificationAreaIcon_HasOpenAndExit_WithAccessKeys_AndIsntShownUntilAsked()
    {
        // Never made visible here, so nothing appears in this PC's notification area.
        using var tray = new TrayIcon(() => { }, () => { });
        Assert.Equal(["&Open Quick Describe", "E&xit Quick Describe"], tray.MenuItems);
        Assert.False(tray.Visible);
    }

    /// <summary>A startup task that's in a given state, and changes to what it's asked (or to <c>after</c>, for one
    /// Windows won't change).</summary>
    private sealed class FakeStartup(SignInStart state, SignInStart? after = null) : IStartAtSignIn
    {
        public List<bool> Asked { get; } = [];

        public Task<SignInStart> GetAsync() => Task.FromResult(state);

        public Task<SignInStart> SetAsync(bool on)
        {
            Asked.Add(on);
            state = after ?? (on ? SignInStart.On : SignInStart.Off);
            return Task.FromResult(state);
        }
    }

    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
