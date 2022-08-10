using Valour.Shared.Items.Messages;

namespace Valour.Server.Database.Items.Messages;

public class MessageAttachment : ISharedMessageAttachment
{
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }
}

