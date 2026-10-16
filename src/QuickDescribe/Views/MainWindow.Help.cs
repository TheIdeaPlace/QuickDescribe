using System.Diagnostics;
using System.Windows;
using QuickDescribe.Services;

namespace QuickDescribe.Views;

/// <summary>The Help menu's updates, bug reports and log, and what happens at start: the "update installed" note, the
/// offer to report a crash, and the quiet daily look for updates. Copied from My Photo Library Described, where they're
/// The Idea Place apps' shared update and bug-report experience.</summary>
public partial class MainWindow
{
    /// <summary>Opens a web page or folder; replaceable in tests.</summary>
    internal static Action<string> OpenUrl { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    private bool OpenInBrowser(string url, string what)
    {
        try
        {
            OpenUrl(url);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowMessage($"The browser couldn't be opened. {what} at {url}.", null);
            return false;
        }
    }

    private void OnGuide(object sender, RoutedEventArgs e) => OpenInBrowser(AppInfo.GuideUrl, "The user guide is");

    // ---- At start ----

    /// <summary>After the window is up: says an update was installed, or else offers to report a crash; then looks for
    /// updates quietly. Never both questions at once.</summary>
    private void AfterStart()
    {
        Dispatcher.BeginInvoke(() =>
        {
            RememberedUpdate();
            ShowUpdatesItem();
            if (SayIfJustUpdated()) ResultBox.Focus();
            else Dispatcher.BeginInvoke(OfferCrashReport, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            _ = LookForUpdatesQuietlyAsync();
            // Quick Describe is meant to run for days, and Windows installs updates only while it isn't running: so the
            // quiet look runs again while it's open (still at most once a day).
            _updateTimer.Tick += (_, _) => _ = LookForUpdatesQuietlyAsync(TimeSpan.Zero);
            _updateTimer.Start();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private readonly System.Windows.Threading.DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };

    private void OfferCrashReport()
    {
        if (App.UncleanLaunch is null) return;
        App.UncleanLaunch = null;
        // The question disables this window, so hotkeys wait for it.
        if (!QuestionDialog.AskSafely(this, AppInfo.Name, $"{AppInfo.Name} quit unexpectedly last time. Report it?",
                "The report opens in a form, where you can read and change it before it's sent. Reports are public.", "_Report…", "Not Now"))
            return;
        _ = ReportBugAsync(App.CrashAttachments, "Quit unexpectedly");
    }

    // ---- Updates ----

    private bool _updating;

    /// <summary>What Help's update item says: an update known to be available, and its version when known.</summary>
    private (bool Available, string? Version) _update;

    private void ShowUpdatesItem() =>
        UpdatesItem.Header = Updates.MenuTitle(Updates.InstalledVersion() ?? Updates.AppVersion, _update.Available, _update.Version);

    /// <summary>An update found earlier and not installed yet is still said after a restart.</summary>
    private void RememberedUpdate()
    {
        if (_settings.AvailableUpdate is { } remembered && Updates.InstalledVersion() is { } running && Updates.IsNewer(remembered, running))
            _update = (true, remembered);
    }

    /// <summary>What a check found out, kept for Help's item and remembered. A check that couldn't reach the feed isn't
    /// counted, so the next launch tries again.</summary>
    private void Found(Updates.State state, string? version)
    {
        if (state is not (Updates.State.UpToDate or Updates.State.Available)) return;
        var running = Updates.InstalledVersion() ?? Updates.AppVersion;
        // An offered version that isn't newer (a feed that went back) isn't an update to show.
        var available = state == Updates.State.Available && (version is null || Updates.IsNewer(version, running));
        if (available && version is null) version = _settings.AvailableUpdate;
        // Said once, when an update is first found, so someone who keeps Quick Describe running hears about it.
        var news = available && !_update.Available && IsActive;
        _update = (available, available ? version : null);
        if (news) Announcer.Announce($"An update to {AppInfo.Name} is available, in the Help menu.");
        _settings.LastUpdateCheck = DateTime.UtcNow;
        _settings.AvailableUpdate = available ? version : null;
        _settings.Save();
        ShowUpdatesItem();
    }

    /// <summary>A quiet look for an update, a few seconds after starting and at most once a day (sooner when the clock was
    /// set back): nothing is said, only Help's item changes. Only for copies that update themselves.</summary>
    internal async Task LookForUpdatesQuietlyAsync(TimeSpan? wait = null)
    {
        if (!Updates.SelfUpdating()) return;
        if (_settings.LastUpdateCheck is { } last && DateTime.UtcNow - last is { } since && since >= TimeSpan.Zero && since < TimeSpan.FromDays(1)) return;
        await Task.Delay(wait ?? TimeSpan.FromSeconds(5));
        var (state, version) = await Updates.QuietCheck();
        // Help > Check for Updates ran meanwhile: its answer is the newer one.
        if (_updating) return;
        Found(state, version);
    }

    /// <summary>The first launch after an update: "Quick Describe Update Installed", with See what's new. Only for installed
    /// copies, and not on a first run or after going back to an older version. True when it was shown.</summary>
    internal bool SayIfJustUpdated()
    {
        if (Updates.InstalledVersion() is not { } current) return false;
        var last = _settings.LastRunVersion;
        if (last == current) return false;
        // Recorded before asking, so it's said once even if the app is closed with the note open.
        _settings.LastRunVersion = current;
        if (_settings.AvailableUpdate is { } pending && !Updates.IsNewer(pending, current)) _settings.AvailableUpdate = null;
        _settings.Save();
        if (!Updates.JustUpdated(last, current)) return false;
        _update = Updates.IsNewer(_update.Version, current) ? _update : (false, null);
        ShowUpdatesItem();
        var shown = Updates.Shown(current, last!).Running;
        // Release notes are per version (v0.2.0); a build of the same version has none of its own, so the list.
        var notes = Updates.ShortVersion(current) == Updates.ShortVersion(last!)
            ? AppInfo.ReleasesUrl
            : AppInfo.ReleaseNotesUrl(Updates.ShortVersion(current));
        if (QuestionDialog.AskSafely(this, $"{AppInfo.Name} Update Installed", $"{AppInfo.Name} is now version {shown}.",
                "See what's new opens the release notes in your browser.", "_See what's new", "OK"))
            OpenInBrowser(notes, "The release notes are");
        return true;
    }

    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        if (_updating)
        {
            Announcer.Announce("Still checking for updates");
            return;
        }
        _updating = true;
        try
        {
            Announcer.Announce("Checking for updates");
            var result = await Updates.CheckAsync();
            Log.Write("update", $"Check for Updates: {result.State} {result.Message}");
            switch (result.State)
            {
                case Updates.State.NoIdentity:
                    Inform($"This copy of {AppInfo.Name} isn't installed, so it can't update itself.");
                    break;
                case Updates.State.NotFromAppInstaller:
                    Inform($"{AppInfo.Name} wasn't installed from its releases page, so Windows doesn't know where its updates are. " +
                           $"Open {AppInfo.InstallerFile} from {AppInfo.ReleasesUrl} to install it again; your settings are kept.");
                    break;
                case Updates.State.UpToDate:
                    Found(result.State, null);
                    Inform($"{AppInfo.Name} is up to date (version {Updates.ShortVersion(Updates.AppVersion)}).");
                    break;
                case Updates.State.Failed:
                    Inform($"{AppInfo.Name} couldn't check for updates. {result.Message}");
                    break;
                case Updates.State.Available:
                    var offered = Updates.OfferedVersionAsync(result.Source!);
                    var install = InstallUpdateAsync(result.Source!);
                    Found(result.State, await offered);
                    await install;
                    break;
            }
        }
        finally
        {
            _updating = false;
        }

        void Inform(string message) => MessageBox.Show(this, message, "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task InstallUpdateAsync(Uri source)
    {
        if (MessageBox.Show(this, $"A new version of {AppInfo.Name} is available. Install it now? {AppInfo.Name} closes, updates, and opens again.",
                "Check for Updates", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes)
            return;
        _requests.Cancel();
        ResultBox.Text = $"Downloading the update. {AppInfo.Name} will close and open again when it's ready.";
        Announcer.Announce(ResultBox.Text);
        if (await Updates.InstallAsync(source) is { } error)
        {
            Log.Write("update", $"The update couldn't be installed: {error}");
            ShowMessage($"The update couldn't be installed. {error}", null);
        }
    }

    // ---- Report a Bug and the log ----

    private void OnReportBug(object sender, RoutedEventArgs e) => _ = ReportBugAsync([], null);

    /// <summary>Help > Report a Bug…, or the offer after a crash with its log attached.</summary>
    private async Task ReportBugAsync(List<BugReport.Attachment> attachments, string? title)
    {
        if (_reporting)
        {
            Announcer.Announce("Still preparing the bug report");
            return;
        }
        _reporting = true;
        _dialogOpen = true;
        try
        {
            // Asking Windows AI what it can do starts the helper, which takes a moment.
            Announcer.Announce("Preparing a bug report");
            var report = new BugReport { SystemText = Privacy.Redact(await SystemCheckTextAsync()) };
            if (title is not null) report.Title = title;
            report.Attachments.AddRange(attachments);
            new BugReportWindow(report, BugReportRelay.Key) { Owner = this }.ShowDialog();
            ResultBox.Focus();
        }
        finally
        {
            _reporting = false;
            _dialogOpen = false;
        }
    }

    private bool _reporting;

    /// <summary>What goes with a bug report: the version, Windows, and what Windows AI says it can do.</summary>
    internal async Task<string> SystemCheckTextAsync()
    {
        var details = await WindowsAI.DetailsAsync(_closing.Token);
        var lines = new List<string>
        {
            $"{AppInfo.Name} {Updates.AppVersion}{(Updates.SelfUpdating() ? "" : " (not installed from the releases page)")}",
            $"Windows {Environment.OSVersion.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}",
            $"Windows AI: {details.State}{(details.HelperVersion is { } v ? $", helper {v}" : "")}",
        };
        if (!details.Usable && details.Message is { } why) lines.Add($"  {why}");
        foreach (var (name, state) in details.Components) lines.Add($"  {name}: {state}");
        lines.Add($"Description type: {_settings.Kind}");
        lines.Add($"Built-in text recognition: {string.Join(", ", Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag))}");
        if (_hotKeys is { Unavailable.Count: > 0 })
            lines.Add($"Hotkeys another app has: {string.Join(", ", _hotKeys.Unavailable.Select(_hotKeys.Map.GestureFor))}");
        var inUse = _hotKeys?.Map ?? _settings.HotKeys;
        lines.Add($"Hotkeys: {SettingsWindow.Summary(inUse)}{(inUse.AllowConflicts ? " Key combinations other apps use: allowed." : "")}");
        return string.Join("\n", lines);
    }

    private void OnOpenLogFolder(object sender, RoutedEventArgs e)
    {
        var folder = AppPaths.ForExplorer(Log.Folder);
        try
        {
            Directory.CreateDirectory(Log.Folder);
            OpenUrl(AppPaths.ForExplorer(Log.Folder));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShowMessage($"File Explorer couldn't open the log folder. It's at {folder}.", null);
        }
    }
}
