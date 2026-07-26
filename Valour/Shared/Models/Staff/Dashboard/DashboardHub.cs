namespace Valour.Shared.Models.Staff;

/// <summary>
/// Contract constants for the staff dashboard realtime channel.
/// Shared between the server hub and the SDK so the group and event
/// names can never drift apart.
/// </summary>
public static class DashboardHub
{
    /// <summary>
    /// SignalR group that staff dashboard viewers join. Joining is gated
    /// server-side by the ValourStaff flag.
    /// </summary>
    public const string Group = "staff-dashboard";

    /// <summary>
    /// Event carrying a <see cref="DashboardLiveStats"/> payload every few seconds.
    /// </summary>
    public const string LiveEvent = "Dashboard-Live";

    /// <summary>
    /// Event carrying a <see cref="DashboardPresenceEvent"/> payload when a user's
    /// first primary connection opens or last one closes.
    /// </summary>
    public const string PresenceEvent = "Dashboard-Presence";
}
