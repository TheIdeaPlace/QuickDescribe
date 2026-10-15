using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace QuickDescribe.Services;

/// <summary>Tells screen readers what happened without moving focus. A UIA notification event says exactly what to
/// speak, and JAWS, NVDA and Narrator all speak it from desktop apps.</summary>
public static class Announcer
{
    /// <summary>For tests: every announcement, whether or not a window is there to raise it from.</summary>
    internal static Action<string>? Observer { get; set; }

    /// <summary>Speaks <paramref name="text"/>, cutting off whatever is being said.</summary>
    public static void Announce(string text) =>
        Raise(text, AutomationNotificationProcessing.ImportantMostRecent, "QuickDescribeAnnouncement");

    private static void Raise(string text, AutomationNotificationProcessing processing, string activityId)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Observer?.Invoke(text);
        // From the window in front: screen readers can ignore notifications from a window that isn't the active one.
        var element = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
        if (element is null) return;
        var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
        try
        {
            peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, processing, text, activityId);
        }
        catch (Exception)
        {
            // An announcement isn't worth failing anything over.
        }
    }
}
