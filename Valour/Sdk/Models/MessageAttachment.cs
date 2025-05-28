using Valour.Shared.Models;

namespace Valour.Sdk.Models;

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
    
    public string Html { get; set; }

    public MessageAttachment(MessageAttachmentType type)
    {
        Type = type;
    }
}

