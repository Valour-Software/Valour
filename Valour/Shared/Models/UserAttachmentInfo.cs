using Valour.Shared.Cdn;

namespace Valour.Shared.Models;

public class UserAttachmentInfo
{
    public string Id { get; set; }
    public string Hash { get; set; }
    public long UserId { get; set; }
    public string MimeType { get; set; }
    public string FileName { get; set; }
    public ContentCategory Category { get; set; }
    public int SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Url { get; set; }
    public string SignedUrl { get; set; }
    public MediaSafetyHashMatchState SafetyHashMatchState { get; set; }
    public DateTime? SafetyHashMatchedAt { get; set; }

    /// <summary>Where the file lives. Only differs from Valour when Google Drive is in use.</summary>
    public AttachmentStorageLocation StorageLocation { get; set; }

    /// <summary>A short reason sending to Drive failed.</summary>
    public string DriveError { get; set; }
}
