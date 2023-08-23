using System.Text.Json.Serialization;
using Valour.Shared.Models;

namespace Valour.Api.Models;

public class MessageAttachment : ISharedMessageAttachment
{
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }

    // Image attributes
    public int Width { get; set; } = 0;
    public int Height { get; set; } = 0;

    public MessageAttachmentType Type { get; set; }
    
    /// <summary>
    /// True if this was an inline attachment - aka, it was generated using urls within the message
    /// </summary>
    public bool Inline { get; set; } = false;
    
    /* Oembed Attributes */
    public string Html { get; set; }

    public MessageAttachment(MessageAttachmentType type)
    {
        Type = type;
    }
    
    /*
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
    */
}

