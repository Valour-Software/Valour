namespace Valour.Shared.Models;
public interface ISharedMessageAttachment
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public int SortOrder { get; set; }
    public string CdnBucketItemId { get; set; }
    public string Location { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }

    public int Width { get; set; }
    public int Height { get; set; }
    public string Data { get; set; }
    public bool Missing { get; set; }
}
