using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace TATAPP.App.Accessibility;

internal static class ScreenReaderAnnouncer
{
    public static void Announce(TextBlock liveRegion, string message, string regionLabel)
    {
        liveRegion.Text = message;
        // JAWS obtains a WPF TextBlock's live content through its UI Automation Name.
        // Updating both the visible text and Name also remains interoperable with NVDA and Narrator.
        AutomationProperties.SetName(liveRegion, $"{regionLabel}. {message}");
        var peer = UIElementAutomationPeer.FromElement(liveRegion)
            ?? UIElementAutomationPeer.CreatePeerForElement(liveRegion);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
