namespace QuickDescribe.Services;

/// <summary>Where Quick Describe keeps its files: settings, the log and the crash flag.</summary>
public static class AppPaths
{
    public static string DataFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.DataFolderName);

    /// <summary>Where a folder of the app's really is, for File Explorer. A packaged app's new folders under
    /// %LOCALAPPDATA% are kept in its package's LocalCache\Local, and only the app sees them at the usual path; Explorer
    /// runs outside the package, so it's given the package's copy when there is one.</summary>
    public static string ForExplorer(string path) =>
        ForExplorer(path, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PackageLocalFolder());

    internal static string ForExplorer(string path, string localAppData, string? packageLocal)
    {
        var root = Path.TrimEndingDirectorySeparator(localAppData) + Path.DirectorySeparatorChar;
        if (packageLocal is null || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return path;
        var moved = Path.Combine(packageLocal, path[root.Length..]);
        return Directory.Exists(moved) ? moved : path;
    }

    private static string? PackageLocalFolder()
    {
        try { return Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Local"); }
        catch (Exception) { return null; } // not installed as a package
    }
}
