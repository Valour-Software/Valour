namespace Valour.Shared.Models;

public enum MessageAttachmentType : long
{
    Image,
    Video,
    Audio,
    File,

    // Future embedded attachments for Valour functions
    ValourMessage,
    ValourInvite,
    ValourPlanet,
    ValourChannel,
    ValourItem,
    ValourEcoAccount,
    ValourEcoTrade,
    ValourReceipt,
    ValourBot,

    // Generic link preview using Open Graph
    SitePreview,

    // Video platforms
    YouTube,
    Vimeo,
    Twitch,
    TikTok,

    // Social platforms
    Twitter,
    Reddit,
    Instagram,
    Bluesky,

    // Music platforms
    Spotify,
    SoundCloud,

    // Developer platforms
    GitHub,
}