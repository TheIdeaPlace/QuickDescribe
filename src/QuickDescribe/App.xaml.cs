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
            _showSignal.Set();
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
        base.OnExit(e);
    }
}
