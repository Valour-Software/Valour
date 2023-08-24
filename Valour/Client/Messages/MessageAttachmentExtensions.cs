using Valour.Api.Models;
using Valour.Client.Components.Messages.Attachments;
using Valour.Shared.Models;

namespace Valour.Client.Messages;

public static class MessageAttachmentExtensions
{
    public static readonly Type[] ComponentLookup = new[]
    {
        typeof(ImageAttachmentComponent), // Image
        typeof(VideoAttachmentComponent), // Video
        typeof(AudioAttachmentComponent), // Audio
        typeof(FileAttachmentComponent), // File
        null, // ValourMessage
        null, // ValourInvite
        null, // ValourPlanet
        null, // ValourChannel
        null, // ValourItem
        null, // ValourEcoAccount
        null, // ValourEcoTrade
        typeof(ReceiptAttachmentComponent), // ValourReceipt
        null, // ValourBot
        null, // SitePreview
        typeof(YoutubeAttachmentComponent), // YouTube
        typeof(VimeoAttachmentComponent), // Vimeo
        typeof(TwitchAttachmentComponent), // Twitch
        typeof(TwitterAttachmentComponent), // Twitter
        typeof(RedditAttachmentComponent), // Reddit
    };


    public static Type GetComponentType(this MessageAttachment attachment)
    {
        return ComponentLookup[(int)attachment.Type];
    }
}
