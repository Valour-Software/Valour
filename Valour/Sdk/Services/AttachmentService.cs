using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Cdn;
using Valour.Shared.Models;
using Valour.Shared.Queries;

namespace Valour.Sdk.Services;

public class AttachmentService : ServiceBase
{
    private static readonly LogOptions LogOptions = new(
        "AttachmentService",
        "#0aa37f",
        "#fc0356",
        "#fc8403"
    );

    private readonly ValourClient _client;

    public AttachmentService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }

    public async Task<TaskResult<QueryResponse<UserAttachmentInfo>>> QueryAsync(QueryRequest request)
    {
        return await _client.PrimaryNode.PostAsyncWithResponse<QueryResponse<UserAttachmentInfo>>(
            "api/attachments/query",
            request);
    }

    public async Task<TaskResult> DeleteAsync(UserAttachmentInfo attachment)
    {
        if (attachment is null)
            return TaskResult.FromFailure("Attachment is required.");

        return await DeleteAsync(attachment.Category, attachment.Hash);
    }

    public async Task<TaskResult> DeleteAsync(ContentCategory category, string hash)
    {
        return await _client.PrimaryNode.DeleteAsync($"api/attachments/{category}/{Uri.EscapeDataString(hash)}");
    }

    /// <summary>
    /// How much of the account's attachment storage is in use.
    /// </summary>
    public async Task<TaskResult<UserStorageInfo>> GetStorageAsync()
    {
        return await _client.PrimaryNode.GetJsonAsync<UserStorageInfo>(
            "api/attachments/storage",
            cacheDurationMs: null);
    }

    // Google Drive //

    /// <summary>The account's Google Drive connection and how files are moving into it.</summary>
    public async Task<TaskResult<DriveConnectionInfo>> GetDriveAsync()
    {
        return await _client.PrimaryNode.GetJsonAsync<DriveConnectionInfo>(
            "api/attachments/drive",
            cacheDurationMs: null);
    }

    /// <summary>
    /// Finishes connecting Google Drive with the ticket and verifier from the
    /// Google consent page.
    /// </summary>
    public async Task<TaskResult<DriveConnectionInfo>> ConnectDriveAsync(string ticket, string verifier)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<DriveConnectionInfo>(
            "api/attachments/drive/connect",
            new DriveConnectRequest { Ticket = ticket, Verifier = verifier });
        ForgetUploadLimit();
        return result;
    }

    /// <summary>Disconnects Google Drive. Files already moved to it stay there.</summary>
    public async Task<TaskResult> DisconnectDriveAsync()
    {
        var result = await _client.PrimaryNode.DeleteAsync("api/attachments/drive");
        ForgetUploadLimit();
        return result;
    }

    private bool? _driveConnected;
    private DateTime _driveCheckedAt;

    /// <summary>
    /// The largest file the account can upload. A connected Google Drive raises
    /// the plan's limit to at least <see cref="GoogleDriveLimits.ConnectedMaxUploadBytes"/>.
    /// Whether Drive is connected is remembered for a few minutes.
    /// </summary>
    public async Task<long> GetMaxUploadBytesAsync()
    {
        var tierLimit = UserSubscriptionTypes.GetMaxUploadBytes(_client.Me?.SubscriptionType);

        if (_driveConnected is null || DateTime.UtcNow - _driveCheckedAt > TimeSpan.FromMinutes(5))
        {
            var storage = await GetStorageAsync();
            if (!storage.Success)
                return tierLimit;

            _driveConnected = storage.Data.DriveConnected;
            _driveCheckedAt = DateTime.UtcNow;
        }

        return _driveConnected == true ? Math.Max(tierLimit, GoogleDriveLimits.ConnectedMaxUploadBytes) : tierLimit;
    }

    /// <summary>Forgets what was learned about Drive, after it was connected or disconnected.</summary>
    public void ForgetUploadLimit() => _driveConnected = null;

    /// <summary>Queues the files uploaded before Drive was connected.</summary>
    public async Task<TaskResult<DriveQueueResult>> CopyExistingToDriveAsync()
    {
        return await _client.PrimaryNode.PostAsyncWithResponse<DriveQueueResult>("api/attachments/drive/copy-existing");
    }

    /// <summary>Tries again the files whose copy to Drive failed.</summary>
    public async Task<TaskResult<DriveQueueResult>> RetryDriveAsync()
    {
        return await _client.PrimaryNode.PostAsyncWithResponse<DriveQueueResult>("api/attachments/drive/retry");
    }

    /// <summary>
    /// The planets and direct messages the account's attachments live in.
    /// </summary>
    public async Task<TaskResult<AttachmentPlacesResponse>> GetCleanupPlacesAsync()
    {
        return await _client.PrimaryNode.GetJsonAsync<AttachmentPlacesResponse>(
            "api/attachments/cleanup/places",
            cacheDurationMs: null);
    }

    /// <summary>
    /// Counts what a cleanup with these criteria would remove.
    /// </summary>
    public async Task<TaskResult<AttachmentCleanupPreview>> PreviewCleanupAsync(AttachmentCleanupCriteria criteria)
    {
        return await _client.PrimaryNode.PostAsyncWithResponse<AttachmentCleanupPreview>(
            "api/attachments/cleanup/preview",
            criteria);
    }

    /// <summary>
    /// Starts a backup archive of the files a cleanup would remove.
    /// </summary>
    public async Task<TaskResult<AttachmentArchiveTicket>> StartCleanupArchiveAsync(AttachmentCleanupCriteria criteria)
    {
        return await _client.PrimaryNode.PostAsyncWithResponse<AttachmentArchiveTicket>(
            "api/attachments/cleanup/archive",
            new AttachmentArchiveRequest { Criteria = criteria });
    }

    public async Task<TaskResult<AttachmentArchiveStatus>> GetCleanupArchiveStatusAsync(string archiveId)
    {
        return await _client.PrimaryNode.GetJsonAsync<AttachmentArchiveStatus>(
            $"api/attachments/cleanup/archive/{Uri.EscapeDataString(archiveId)}/status",
            cacheDurationMs: null);
    }

    /// <summary>
    /// The full URL a browser can open to download a backup archive.
    /// </summary>
    public string GetCleanupArchiveUrl(AttachmentArchiveTicket ticket) =>
        _client.PrimaryNode.NodeBaseAddress.TrimEnd('/') + "/" + ticket.DownloadPath.TrimStart('/');

    /// <summary>
    /// Deletes one batch of the files matching the criteria. Call again until
    /// <see cref="AttachmentCleanupDeleteResult.Remaining"/> reaches zero.
    /// </summary>
    public async Task<TaskResult<AttachmentCleanupDeleteResult>> DeleteCleanupBatchAsync(AttachmentCleanupDeleteRequest request)
    {
        return await _client.PrimaryNode.PostAsyncWithResponse<AttachmentCleanupDeleteResult>(
            "api/attachments/cleanup/delete",
            request);
    }
}
