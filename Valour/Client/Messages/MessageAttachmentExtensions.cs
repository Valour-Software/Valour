using Valour.Api.Models;
using Valour.Client.Components.Messages.Attachments;
using Valour.Shared.Models;

namespace Valour.Client.Messages;

public static class MessageAttachmentExtensions
{
    public static Type GetComponentType(this MessageAttachment attachment)
    {
        switch (attachment.Type)
        {
            case MessageAttachmentType.Image:
                return typeof(ImageAttachmentComponent);
            case MessageAttachmentType.Video:
                return typeof(VideoAttachmentComponent);
            case MessageAttachmentType.Audio:
                return typeof(AudioAttachmentComponent);
            case MessageAttachmentType.YouTube:
                return typeof(YoutubeAttachmentComponent);
            case MessageAttachmentType.Vimeo:
                return typeof(VimeoAttachmentComponent);
            case MessageAttachmentType.Twitch:
                return typeof(TwitchAttachmentComponent);
            default:
                return typeof(FileAttachmentComponent);
        }
    }
}
