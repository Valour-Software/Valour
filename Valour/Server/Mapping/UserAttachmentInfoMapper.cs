using Valour.Shared.Models;

namespace Valour.Server.Mapping;

public static class UserAttachmentInfoMapper
{
    public static UserAttachmentInfo ToInfo(this Valour.Database.CdnBucketItem item)
    {
        if (item is null)
            return null;

        return new UserAttachmentInfo()
        {
            Id = item.Id,
            Hash = item.Hash,
            UserId = item.UserId,
            MimeType = item.MimeType,
            FileName = item.FileName,
            Category = item.Category,
            SizeBytes = item.SizeBytes,
            CreatedAt = item.CreatedAt,
            Url = $"https://cdn.valour.gg/content/{item.Id}",
            SafetyHashMatchState = item.SafetyHashMatchState,
            SafetyHashMatchedAt = item.SafetyHashMatchedAt
        };
    }
}
