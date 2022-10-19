using Valour.Shared.Items.Messages;

namespace Valour.Api.Items.Messages.Attachments;

public enum AttachmentType
{
    None,
    Image,
    Video,
    Audio,
    File
}

public class MessageAttachment : ISharedMessageAttachment
{
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }

    // Image attributes
    public int Width { get; set; } = 1000;
    public int Height { get; set; } = 1000;

    public AttachmentType GetAttachmentType()
    {
        // Malformed
        if (string.IsNullOrWhiteSpace(MimeType))
            return AttachmentType.None;

        if (MimeType.StartsWith("image"))
            return AttachmentType.Image;
        else if (MimeType.StartsWith("audio"))
            return AttachmentType.Audio;
        else if (MimeType.StartsWith("video"))
            return AttachmentType.Video;

        return AttachmentType.File;
    }
}

