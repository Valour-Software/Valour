using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Cdn;
using Valour.Shared.Queries;

namespace Valour.Server.Api.Dynamic;

public class AttachmentApi
{
    [ValourRoute(HttpVerbs.Post, "api/attachments/query")]
    [UserRequired]
    public static async Task<IResult> QueryAttachmentsAsync(
        [FromBody] QueryRequest? queryRequest,
        TokenService tokenService,
        UserAttachmentService attachmentService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var result = await attachmentService.QueryAsync(token.UserId, queryRequest ?? new QueryRequest());
        return Results.Json(result);
    }

    [ValourRoute(HttpVerbs.Delete, "api/attachments/{category}/{hash}")]
    [UserRequired]
    public static async Task<IResult> DeleteAttachmentAsync(
        ContentCategory category,
        string hash,
        TokenService tokenService,
        UserAttachmentService attachmentService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var result = await attachmentService.DeleteAsync(token.UserId, category, hash);

        if (!result.Success)
        {
            if (result.Code == 404)
                return ValourResult.NotFound(result.Message);

            return ValourResult.BadRequest(result.Message);
        }

        return ValourResult.Ok();
    }
}
