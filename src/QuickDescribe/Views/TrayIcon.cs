using QuickDescribe.Services;

namespace QuickDescribe.Views;

/// <summary>
/// Quick Describe's notification area icon, shown the whole time Settings' "Keep running" is on. WPF has none, so this is the one use of WinForms, written in full. Copied from QuickMail's TrayIconManager.
/// Choosing the icon (a click, or Enter from the keyboard, which Windows sends as a left click) opens the window; its
/// menu has Open and Exit. Both are run on WPF's dispatcher, after WinForms' handler returns: an exception then reaches
/// the app's own handler (logged, and said) rather than WinForms' stack-trace dialog, and Exit doesn't dispose the menu
/// while its own click is still being handled.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _picture = AppIcon();

    public TrayIcon(Action open, Action exit)
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        void Later(Action action) => dispatcher.BeginInvoke(action);
        _icon = new System.Windows.Forms.NotifyIcon { Text = AppInfo.Name, Icon = _picture, Visible = false };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) Later(open);
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem($"&Open {AppInfo.Name}");
        openItem.Click += (_, _) => Later(open);
        var exitItem = new System.Windows.Forms.ToolStripMenuItem($"E&xit {AppInfo.Name}");
        exitItem.Click += (_, _) => Later(exit);
        menu.Items.Add(openItem);
        menu.Items.Add(exitItem);
        _icon.ContextMenuStrip = menu;
    }

    /// <summary>The menu's items, as they read.</summary>
    internal IEnumerable<string> MenuItems => _icon.ContextMenuStrip!.Items.Cast<System.Windows.Forms.ToolStripItem>().Select(i => i.Text ?? "");

    internal bool Visible => _icon.Visible;

    public void Show() => _icon.Visible = true;

    public void Hide() => _icon.Visible = false;

    /// <summary>The app's icon at the notification area's size (QuickDescribe.ico has 16, 24, 32, 48 and 256), rather
    /// than the 32-pixel one shrunk.</summary>
    private static System.Drawing.Icon AppIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe &&
                System.Drawing.Icon.ExtractIcon(exe, 0, System.Windows.Forms.SystemInformation.SmallIconSize.Width) is { } icon)
                return icon;
        }
        catch (Exception ex)
        {
            Log.Write("tray", $"The app's icon couldn't be read: {ex.Message}");
        }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        // Windows' generic icon, when the app's couldn't be read, is shared: not ours to dispose.
        if (!ReferenceEquals(_picture, System.Drawing.SystemIcons.Application)) _picture.Dispose();
    }
}
