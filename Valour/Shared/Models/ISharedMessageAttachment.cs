namespace Valour.Shared.Models;
public interface ISharedMessageAttachment
{
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }
}
