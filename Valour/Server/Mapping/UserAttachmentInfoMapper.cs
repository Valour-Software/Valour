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
            Url = $"{ValourHosts.ContentCdnBaseUrl}/content/{item.Id}",
            SafetyHashMatchState = item.SafetyHashMatchState,
            SafetyHashMatchedAt = item.SafetyHashMatchedAt,
            StorageLocation = GetLocation(item),
            StorageExpiresAt = item.StoragePurgedAt is null ? item.StorageExpiresAt : null,
            DriveError = item.DriveState == DriveSyncState.Failed ? item.DriveError : null,
        };
    }

    private static AttachmentStorageLocation GetLocation(Valour.Database.CdnBucketItem item) => item.DriveState switch
    {
        DriveSyncState.Pending or DriveSyncState.Syncing => AttachmentStorageLocation.SendingToDrive,
        DriveSyncState.Failed => AttachmentStorageLocation.DriveFailed,
        DriveSyncState.Synced when item.StoragePurgedAt is not null => AttachmentStorageLocation.DriveOnly,
        DriveSyncState.Synced => AttachmentStorageLocation.DriveWithValourCopy,
        _ => AttachmentStorageLocation.Valour,
    };
}
