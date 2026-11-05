namespace Valour.Shared;

/// <summary>
/// Names of the events the server sends to clients over the core SignalR hub.
/// Installed apps listen for these exact names, so an event must keep its name
/// and keep being sent for as long as a supported client version listens for
/// it. The wire contract test records this list and fails when a released
/// client's event disappears. See Docs/ClientCompatibility.md.
/// </summary>
public static class HubEvents
{
    // Messages

    /// <summary>A new planet channel message.</summary>
    public const string Relay = "Relay";

    /// <summary>An edited planet channel message.</summary>
    public const string RelayEdit = "RelayEdit";

    /// <summary>A new direct message, sent to each participant's user group.</summary>
    public const string RelayDirect = "RelayDirect";

    /// <summary>An edited direct message.</summary>
    public const string RelayDirectEdit = "RelayDirectEdit";

    /// <summary>A deleted planet or direct message.</summary>
    public const string DeleteMessage = "DeleteMessage";

    public const string MessageReactionAdd = "MessageReactionAdd";
    public const string MessageReactionRemove = "MessageReactionRemove";

    /// <summary>An embed update shown only to one user.</summary>
    public const string PersonalEmbedUpdate = "Personal-Embed-Update";

    /// <summary>An embed update shown to everyone in a channel.</summary>
    public const string ChannelEmbedUpdate = "Channel-Embed-Update";

    /// <summary>An embed interaction, sent to the message author's user group.</summary>
    public const string InteractionEvent = "InteractionEvent";

    // Channels

    public const string ChannelsMoved = "Channels-Moved";
    public const string ChannelWatchingUpdate = "Channel-Watching-Update";
    public const string ChannelCurrentlyTypingUpdate = "Channel-CurrentlyTyping-Update";

    /// <summary>The time of a channel's latest message, used for unread state.</summary>
    public const string ChannelState = "Channel-State";

    public const string UserChannelStateUpdate = "UserChannelState-Update";
    public const string DirectChannelUpdate = "Direct-Channel-Update";
    public const string DirectChannelRemoved = "Direct-Channel-Removed";

    // Planets and roles

    public const string RoleMembershipHashUpdate = "RoleMembershipHash-Update";
    public const string RoleOrderUpdate = "RoleOrder-Update";

    // Account

    /// <summary>The session ended. The payload is the reason, such as "disabled" or "revoked".</summary>
    public const string ForceLogout = "ForceLogout";

    public const string RelayNotification = "RelayNotification";
    public const string RelayNotificationsCleared = "RelayNotificationsCleared";
    public const string RelayFriendEvent = "RelayFriendEvent";
    public const string TransactionProcessed = "Transaction-Processed";
    public const string PlatformBannerUpdate = "PlatformBanner-Update";

    // Voice and calls

    public const string VoiceChannelParticipants = "Voice-Channel-Participants";
    public const string VoiceSessionReplace = "Voice-Session-Replace";
    public const string VoiceModerationAction = "Voice-Moderation-Action";
    public const string DirectCallUpdate = "Direct-Call-Update";

    // Villages

    public const string VillagePresenceJoined = "Village-Presence-Joined";
    public const string VillagePresenceMoved = "Village-Presence-Moved";
    public const string VillagePresenceLeft = "Village-Presence-Left";

    // Encryption and staff

    /// <summary>An end-to-end encryption notification, such as a new channel key.</summary>
    public const string E2eeEvent = "E2ee-Event";

    /// <summary>Staff dashboard statistics, sent every few seconds.</summary>
    public const string DashboardLive = "Dashboard-Live";

    /// <summary>A staff dashboard presence change.</summary>
    public const string DashboardPresence = "Dashboard-Presence";

    // Models

    /// <summary>
    /// The event carrying an updated model and its insertion flags. Clients
    /// register one per model type, named after the type, such as
    /// "Channel-Update". Renaming a model type therefore renames its events.
    /// </summary>
    public static string ModelUpdate(string modelTypeName) => modelTypeName + "-Update";

    /// <summary>The event carrying a deleted model, such as "Channel-Delete".</summary>
    public static string ModelDelete(string modelTypeName) => modelTypeName + "-Delete";
}
