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
    
    SitePreview,
    
    YouTube,
    Vimeo,
    Twitch,
    Twitter,
    Reddit,
}