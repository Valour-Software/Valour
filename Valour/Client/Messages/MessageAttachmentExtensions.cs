using Valour.Api.Items.Messages.Attachments;
using Valour.Client.Components.Messages.Attachments;

namespace Valour.Client.Messages;

public static class MessageAttachmentExtensions
{
    public static Type GetComponentType(this MessageAttachment attachment)
    {
        var type = attachment.GetAttachmentType();
        switch (type)
        {
            case AttachmentType.Image:
                return typeof(ImageAttachmentComponent);
            case AttachmentType.Video:
                return typeof(VideoAttachmentComponent);
            case AttachmentType.Audio:
                return typeof(AudioAttachmentComponent);
            default:
                return typeof(FileAttachmentComponent);
        }
    }
}
