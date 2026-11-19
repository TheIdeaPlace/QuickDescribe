using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace QuickDescribe.Services;

/// <summary>Tells screen readers what happened without moving focus. A UIA notification event says exactly what to
/// speak, and JAWS, NVDA and Narrator all speak it from desktop apps.</summary>
public static class Announcer
{
    /// <summary>Speaks <paramref name="text"/>, cutting off whatever is being said, including the last announcement
    /// ("Describing…" gives way to the result). <paramref name="after"/>: said after what's being said instead, for a
    /// note that mustn't cut off the result it belongs to.</summary>
    public static void Announce(string text, bool after = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        // From the window in front: screen readers can ignore notifications from a window that isn't the active one.
        var element = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        if (element is null) return;
        var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
        try
        {
            peer?.RaiseNotificationEvent(AutomationNotificationKind.Other,
                after ? AutomationNotificationProcessing.ImportantAll : AutomationNotificationProcessing.ImportantMostRecent, text,
                after ? "QuickDescribeNote" : "QuickDescribeAnnouncement");
        }
        catch (Exception)
        {
            // An announcement isn't worth failing anything over.
        }
    }
}
