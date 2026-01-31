using Valour.Sdk.Models;
using Valour.Client.Components.Messages.Attachments;

namespace Valour.Client.Messages;

public static class MessageAttachmentExtensions
{
    /// <summary>
    /// Maps MessageAttachmentType enum values to their corresponding Blazor components.
    /// IMPORTANT: The order must match the MessageAttachmentType enum exactly!
    /// </summary>
    public static readonly Type[] ComponentLookup = new[]
    {
        typeof(ImageAttachmentComponent),       // 0: Image
        typeof(VideoAttachmentComponent),       // 1: Video
        typeof(AudioAttachmentComponent),       // 2: Audio
        typeof(FileAttachmentComponent),        // 3: File
        null,                                   // 4: ValourMessage
        typeof(InviteAttachmentComponent),      // 5: ValourInvite
        null,                                   // 6: ValourPlanet
        null,                                   // 7: ValourChannel
        null,                                   // 8: ValourItem
        null,                                   // 9: ValourEcoAccount
        null,                                   // 10: ValourEcoTrade
        typeof(ReceiptAttachmentComponent),     // 11: ValourReceipt
        null,                                   // 12: ValourBot
        typeof(SitePreviewAttachmentComponent), // 13: SitePreview
        typeof(YoutubeAttachmentComponent),     // 14: YouTube
        typeof(VimeoAttachmentComponent),       // 15: Vimeo
        typeof(TwitchAttachmentComponent),      // 16: Twitch
        typeof(TikTokAttachmentComponent),      // 17: TikTok
        typeof(TwitterAttachmentComponent),     // 18: Twitter
        typeof(RedditAttachmentComponent),      // 19: Reddit
        typeof(InstagramAttachmentComponent),   // 20: Instagram
        typeof(BlueskyAttachmentComponent),     // 21: Bluesky
        typeof(SpotifyAttachmentComponent),     // 22: Spotify
        typeof(SoundCloudAttachmentComponent),  // 23: SoundCloud
        typeof(GitHubAttachmentComponent),      // 24: GitHub
    };

    public static Type GetComponentType(this MessageAttachment attachment)
    {
        var index = (int)attachment.Type;
        if (index < 0 || index >= ComponentLookup.Length)
            return null;

        return ComponentLookup[index];
    }
}
