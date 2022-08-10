namespace Valour.Shared.Items.Messages;
public interface ISharedMessageAttachment
{
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }
}
