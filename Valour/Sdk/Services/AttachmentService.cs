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
}
