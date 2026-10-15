using System.Windows;

namespace QuickDescribe;

public partial class App : Application
{
    private const string InstanceName = @"Local\TheIdeaPlace.QuickDescribe";
    private Mutex? _instance;
    private EventWaitHandle? _showSignal;

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
            Services.Native.AllowSetForegroundWindow(Services.Native.ASFW_ANY);
            _showSignal.Set();
            _showSignal.Dispose();
            Shutdown();
            return;
        }

        var window = new Views.MainWindow();
        MainWindow = window;
        window.Show();

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
        _instance?.Dispose();
        _showSignal?.Dispose();
        base.OnExit(e);
    }
}
