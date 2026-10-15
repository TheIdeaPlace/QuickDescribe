namespace QuickDescribe;

/// <summary>
/// The app's name and addresses, in one place. Everything the app says uses <see cref="Name"/>, and
/// scripts\build-ai-helper.ps1 reads it from here for the helper package's display name. Also compiled into the AI helper.
/// </summary>
internal static class AppInfo
{
    public const string Name = "Quick Describe";

    /// <summary>The Windows AI helper's name, as people see it.</summary>
    public const string HelperName = Name + " AI helper";

    /// <summary>The folder under %LOCALAPPDATA% for settings. Mustn't follow <see cref="Name"/> if the app is renamed
    /// after a release, or saved settings are lost.</summary>
    public const string DataFolderName = "Quick Describe";

    /// <summary>The installer people open, and where every copy looks for updates (the latest release's).</summary>
    public const string InstallerFile = "QuickDescribe.appinstaller";

    public const string Repository = "TheIdeaPlace/QuickDescribe";
}
