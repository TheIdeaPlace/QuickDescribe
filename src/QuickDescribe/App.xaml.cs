using System.Windows;
using QuickDescribe.Services;

namespace QuickDescribe;

public partial class App : Application
{
    private const string InstanceName = @"Local\TheIdeaPlace.QuickDescribe";
    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private bool _running;

    /// <summary>When the last run started, if it didn't quit cleanly; cleared once the offer to report it is made.</summary>
    public static DateTime? UncleanLaunch { get; set; }

    /// <summary>The end of the crashed run's log, for the crash report.</summary>
    public static List<BugReport.Attachment> CrashAttachments { get; set; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One copy at a time: a second couldn't have the hotkeys. Starting it again brings the first to the front.
        _instance = new Mutex(true, InstanceName, out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        if (!first)
        {
            // This copy was just started by the person, so it may bring a window to the front; the first copy, signalled
            // from here, hasn't had input and Windows would only flash its taskbar button. Hand the right on.
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);
            _showSignal.Set();
            _showSignal.Dispose();
            Shutdown();
            return;
        }
        _running = true;

        // Whether the last run quit cleanly: offered as a report once the window is up, with the end of the log read now,
        // before this run adds to it.
        UncleanLaunch = CrashCheck.Begin();
        if (UncleanLaunch is not null) CrashAttachments = CrashCheck.Attachments();
        Log.RemoveOld();
        Log.Write("start", $"{AppInfo.Name} {Updates.AppVersion} on Windows {Environment.OSVersion.Version}, " +
                           $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        if (UncleanLaunch is not null) Log.Write("start", "The last run didn't quit cleanly");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Log.Crash("crash", "Unhandled", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => Log.Crash("error", "A background task failed unseen", args.Exception);
        // Something unexpected in an event handler is said and logged, not a silent crash.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Crash("error", "Unhandled in the window", args.Exception);
            var owner = Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? MainWindow;
            MessageBox.Show(owner, $"Something went wrong: {args.Exception.Message}", AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        // Windows signing out or restarting: WPF only queues Shutdown for this, and Windows may end the process before
        // OnExit runs, which would leave the flag and offer a crash report at the next start. Quick Describe is meant to be
        // running all the time, so that would be almost every restart.
        SessionEnding += (_, _) =>
        {
            Log.Write("exit", "Windows is signing out or shutting down");
            (MainWindow as Views.MainWindow)?.AllowClose();
            CrashCheck.End();
        };

        var window = new Views.MainWindow();
        MainWindow = window;
        // Started by Windows at sign-in: out of the way. Started by the person: in front.
        if (StartAtSignIn.StartedBySignIn()) window.StartQuietly();
        else window.Show();

        var signal = _showSignal;
        new Thread(() =>
        {
            while (true)
            {
                try { signal.WaitOne(); }
                catch (ObjectDisposedException) { return; }
                Dispatcher.BeginInvoke(window.BringToFront);
            }
        }) { IsBackground = true, Name = "Show signal" }.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Only the copy that's running: a second copy that handed over and quit says nothing about crashes.
        if (_running) CrashCheck.End();
        _instance?.Dispose();
        _showSignal?.Dispose();
        base.OnExit(e);
    }
}
