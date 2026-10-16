using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace QuickDescribe.Services;

/// <summary>Whether Windows starts Quick Describe at sign-in, as Settings shows it.</summary>
public enum SignInStart
{
    /// <summary>Not an installed copy: only a packaged app has a startup task.</summary>
    Unavailable,
    Off,
    On,
    /// <summary>Turned off in Task Manager's Startup apps or Settings, Apps, Startup: only the person can turn it on
    /// again, there.</summary>
    OffInWindows,
    OffByPolicy,
    OnByPolicy,
}

/// <summary>Starting at sign-in, through Windows' startup task: Quick Describe is packaged, so the package's startup task
/// (AppxManifest.xml's windows.startupTask), not a Run key. A packaged app's own writes to the Run key go to its private
/// copy of the registry, which Windows never reads at sign-in. The task is listed in Task Manager's Startup apps and in
/// Settings, Apps, Startup, where it can be turned off as well.</summary>
public interface IStartAtSignIn
{
    Task<SignInStart> GetAsync();

    /// <summary>Turns it on or off, and gives what it is afterwards.</summary>
    Task<SignInStart> SetAsync(bool on);
}

public sealed class StartAtSignIn : IStartAtSignIn
{
    /// <summary>The TaskId in AppxManifest.xml.</summary>
    public const string TaskId = "QuickDescribeAtSignIn";

    public async Task<SignInStart> GetAsync() => await TaskAsync() is { } task ? From(task.State) : SignInStart.Unavailable;

    public async Task<SignInStart> SetAsync(bool on)
    {
        if (await TaskAsync() is not { } task) return SignInStart.Unavailable;
        try
        {
            if (on) return From(await task.RequestEnableAsync());
            if (task.State == StartupTaskState.Enabled) task.Disable();
            return From(task.State);
        }
        catch (Exception ex)
        {
            Log.Write("settings", $"The startup task couldn't be changed: {ex.GetType().Name}: {ex.Message}");
            return From(task.State);
        }
    }

    private static async Task<StartupTask?> TaskAsync()
    {
        if (Updates.PackageVersion is null) return null;
        try
        {
            return await StartupTask.GetAsync(TaskId);
        }
        catch (Exception ex)
        {
            Log.Write("settings", $"The startup task isn't there: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    internal static SignInStart From(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => SignInStart.On,
        StartupTaskState.EnabledByPolicy => SignInStart.OnByPolicy,
        StartupTaskState.DisabledByUser => SignInStart.OffInWindows,
        StartupTaskState.DisabledByPolicy => SignInStart.OffByPolicy,
        _ => SignInStart.Off,
    };

    /// <summary>Whether the check box shows it on.</summary>
    public static bool IsOn(SignInStart state) => state is SignInStart.On or SignInStart.OnByPolicy;

    /// <summary>What to say when Settings' choice didn't take, or null when it did.</summary>
    public static string? Outcome(bool wanted, SignInStart result) => (wanted, result) switch
    {
        (true, SignInStart.OffInWindows) =>
            $"{AppInfo.Name} is turned off in Windows' startup apps, so it can't turn itself on. Turn it on in Windows Settings, under Apps, Startup.",
        (true, SignInStart.OffByPolicy) => $"Your organization doesn't let {AppInfo.Name} start when you sign in.",
        (true, SignInStart.Unavailable) => $"This copy of {AppInfo.Name} isn't installed, so Windows can't start it when you sign in.",
        (true, SignInStart.Off) => $"Windows didn't turn on starting {AppInfo.Name} when you sign in.",
        (false, SignInStart.OnByPolicy) => $"Your organization starts {AppInfo.Name} when you sign in, so it can't be turned off here.",
        (false, SignInStart.On) => $"Windows didn't turn off starting {AppInfo.Name} when you sign in. Turn it off in Windows Settings, under Apps, Startup.",
        _ => null,
    };

    /// <summary>Whether Windows started this run at sign-in, rather than the person.</summary>
    public static bool StartedBySignIn()
    {
        if (Updates.PackageVersion is null) return false;
        try
        {
            return AppInstance.GetActivatedEventArgs() is { Kind: ActivationKind.StartupTask };
        }
        catch (Exception)
        {
            return false;
        }
    }
}
