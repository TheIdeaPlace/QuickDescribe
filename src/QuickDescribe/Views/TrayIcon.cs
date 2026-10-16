using QuickDescribe.Services;

namespace QuickDescribe.Views;

/// <summary>
/// Quick Describe's notification area icon, shown while it keeps running with its window closed (Settings' "Keep
/// running"). WPF has none, so this is the one use of WinForms, written in full. Copied from QuickMail's TrayIconManager.
/// Choosing the icon (a click, or Enter from the keyboard, which Windows sends as a left click) opens the window; its
/// menu has Open and Exit.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public TrayIcon(Action open, Action exit)
    {
        _icon = new System.Windows.Forms.NotifyIcon { Text = AppInfo.Name, Icon = AppIcon(), Visible = false };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left) open();
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem($"&Open {AppInfo.Name}");
        openItem.Click += (_, _) => open();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem($"E&xit {AppInfo.Name}");
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(openItem);
        menu.Items.Add(exitItem);
        _icon.ContextMenuStrip = menu;
    }

    public void Show() => _icon.Visible = true;

    public void Hide() => _icon.Visible = false;

    private static System.Drawing.Icon AppIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon) return icon;
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
    }
}
