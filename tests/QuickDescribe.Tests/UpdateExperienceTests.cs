using QuickDescribe.Services;

namespace QuickDescribe.Tests;

/// <summary>The Idea Place apps' shared update experience (The-Idea-Place-Projects#12), as in My Photo Library Described:
/// Help always says what's running or what's available, and the first launch after an update says so. (The window's use
/// of it is checked by hand: it opens windows.)</summary>
public sealed class UpdateExperienceTests
{
    [Fact]
    public void HelpsItem_SaysWhatsRunning_OrWhatsAvailable()
    {
        Assert.Equal("Check for _Updates (running 0.2.0)…", Updates.MenuTitle("0.2.0.310", false, null));
        Assert.Equal("_Update to 0.3.0 available…", Updates.MenuTitle("0.2.0.310", true, "0.3.0.400"));
        // Only the build differs (test builds): the full numbers, so it doesn't read "Update to 0.2.0" while running 0.2.0.
        Assert.Equal("_Update to 0.2.0.320 available…", Updates.MenuTitle("0.2.0.310", true, "0.2.0.320"));
        Assert.Equal("_Update Available…", Updates.MenuTitle("0.2.0.310", true, null));
    }

    [Theory]
    [InlineData(null, "0.2.0.310", false)]          // first run
    [InlineData("0.2.0.310", "0.2.0.310", false)]   // same version
    [InlineData("0.3.0.400", "0.2.0.310", false)]   // went back to an older one
    [InlineData("0.2.0.310", "0.2.0.320", true)]
    [InlineData("0.1.1.290", "0.2.0.310", true)]
    [InlineData("not a version", "0.2.0.310", false)]
    public void TheUpdateInstalledNote_IsOnlyForANewerVersionThanLastRan(string? last, string current, bool shown) =>
        Assert.Equal(shown, Updates.JustUpdated(last, current));

    [Fact]
    public void TheAvailableVersion_IsReadFromTheAppInstallerFile()
    {
        Assert.Equal("0.3.0.400", Updates.VersionIn("""
            <AppInstaller xmlns="http://schemas.microsoft.com/appx/appinstaller/2021" Version="0.3.0.400" Uri="https://example.com/QuickDescribe.appinstaller">
              <MainBundle Name="TheIdeaPlace.QuickDescribe" Publisher="CN=x" Version="0.3.0.400" Uri="https://example.com/a.msixbundle" />
            </AppInstaller>
            """));
        Assert.Null(Updates.VersionIn("not xml"));
        // The bundle's version, and only one that reads as a version.
        Assert.Equal("0.3.0.400", Updates.VersionIn("""<AppInstaller Version="9.9.9.9"><MainBundle Version="0.3.0.400" /></AppInstaller>"""));
        Assert.Null(Updates.VersionIn("""<AppInstaller><MainBundle Version="" /></AppInstaller>"""));
        Assert.True(Updates.IsNewer("0.3.0.400", "0.2.0.310"));
        Assert.False(Updates.IsNewer("0.2.0.310", "0.2.0.310"));
        Assert.False(Updates.IsNewer("junk", "0.2.0.310"));
    }

    [Fact]
    public void TheRepository_IsInOnePlace()
    {
        Assert.Equal("https://github.com/TheIdeaPlace/QuickDescribe/issues/new", AppInfo.IssuesUrl);
        Assert.Equal("https://github.com/TheIdeaPlace/QuickDescribe/releases/tag/v0.2.0", AppInfo.ReleaseNotesUrl("0.2.0"));
        Assert.Equal("0.2.0", Updates.ShortVersion("0.2.0.310"));
    }
}
