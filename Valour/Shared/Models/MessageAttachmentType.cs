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

    // Valour-native structured attachment
    Embed,

    // Inline preview of a Valour thread linked in chat
    ValourThread,

    // Inline preview of a Valour wiki page linked in chat
    ValourWikiPage,
}
