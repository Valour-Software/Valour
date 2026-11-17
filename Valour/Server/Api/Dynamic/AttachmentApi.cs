using Microsoft.AspNetCore.Mvc;
using Valour.Server.Services.GoogleDrive;
using Valour.Shared;
using Valour.Shared.Cdn;
using Valour.Shared.Models;
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

    [ValourRoute(HttpVerbs.Get, "api/attachments/storage")]
    [UserRequired]
    public static async Task<IResult> GetStorageAsync(
        TokenService tokenService,
        ValourDb db)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(await UserStorageService.GetInfoAsync(db, token.UserId));
    }

    // Google Drive //

    [ValourRoute(HttpVerbs.Get, "api/attachments/drive")]
    [UserRequired]
    public static async Task<IResult> GetDriveAsync(
        TokenService tokenService,
        UserDriveService driveService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(await driveService.GetInfoAsync(token.UserId));
    }

    /// <summary>
    /// Finishes connecting Google Drive. The ticket and verifier come from the
    /// Google consent page, started with the ConnectDrive intent, and only the
    /// session that started that flow can redeem them.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/attachments/drive/connect")]
    [UserRequired]
    public static async Task<IResult> ConnectDriveAsync(
        [FromBody] DriveConnectRequest? request,
        TokenService tokenService,
        ExternalAuthService externalAuth,
        UserDriveService driveService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Ticket) || string.IsNullOrWhiteSpace(request.Verifier))
            return ValourResult.BadRequest("Include the ticket and verifier.");

        var token = await tokenService.GetCurrentTokenAsync();
        var grant = await externalAuth.TakeGrantAsync(request.Ticket, request.Verifier, ExternalAuthIntent.ConnectDrive, token.UserId);
        if (grant is null)
            return ValourResult.BadRequest("This link has expired. Try again.");

        var result = await driveService.ConnectAsync(token.UserId, grant.ProviderUserId, grant.Label, grant.RefreshToken);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(await driveService.GetInfoAsync(token.UserId));
    }

    [ValourRoute(HttpVerbs.Delete, "api/attachments/drive")]
    [UserRequired]
    public static async Task<IResult> DisconnectDriveAsync(
        TokenService tokenService,
        UserDriveService driveService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var result = await driveService.DisconnectAsync(token.UserId);
        return result.Success ? ValourResult.Ok() : ValourResult.BadRequest(result.Message);
    }

    /// <summary>Chooses whether new uploads go to Drive or stay on Valour.</summary>
    [ValourRoute(HttpVerbs.Post, "api/attachments/drive/default")]
    [UserRequired]
    public static async Task<IResult> SetDriveDefaultAsync(
        [FromBody] DriveDefaultRequest? request,
        TokenService tokenService,
        UserDriveService driveService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include the setting.");

        var token = await tokenService.GetCurrentTokenAsync();
        var result = await driveService.SetNewUploadsToDriveAsync(token.UserId, request.NewUploadsToDrive);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(await driveService.GetInfoAsync(token.UserId));
    }

    /// <summary>Sends one file to Drive and removes its copy on Valour.</summary>
    [ValourRoute(HttpVerbs.Post, "api/attachments/{category}/{hash}/to-drive")]
    [UserRequired]
    public static async Task<IResult> SendToDriveAsync(
        ContentCategory category,
        string hash,
        TokenService tokenService,
        DriveSyncService driveSync)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var result = await driveSync.SendToDriveAsync(token.UserId, category, hash);
        return ToMoveResult(result);
    }

    /// <summary>Makes Valour the home of one file that is in Drive.</summary>
    [ValourRoute(HttpVerbs.Post, "api/attachments/{category}/{hash}/to-valour")]
    [UserRequired]
    public static async Task<IResult> MoveToValourAsync(
        ContentCategory category,
        string hash,
        TokenService tokenService,
        DriveSyncService driveSync)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var result = await driveSync.MoveToValourAsync(token.UserId, category, hash);
        return ToMoveResult(result);
    }

    private static IResult ToMoveResult(TaskResult<UserAttachmentInfo> result)
    {
        if (result.Success)
            return Results.Json(result.Data);

        return result.Code == 404
            ? ValourResult.NotFound(result.Message)
            : ValourResult.BadRequest(result.Message);
    }

    /// <summary>Queues the files uploaded before Drive was connected.</summary>
    [ValourRoute(HttpVerbs.Post, "api/attachments/drive/copy-existing")]
    [UserRequired]
    public static async Task<IResult> CopyExistingToDriveAsync(
        TokenService tokenService,
        UserDriveService driveService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(new DriveQueueResult { Queued = await driveService.QueueExistingFilesAsync(token.UserId) });
    }

    /// <summary>Tries again the files whose copy to Drive failed.</summary>
    [ValourRoute(HttpVerbs.Post, "api/attachments/drive/retry")]
    [UserRequired]
    public static async Task<IResult> RetryDriveAsync(
        TokenService tokenService,
        UserDriveService driveService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(new DriveQueueResult { Queued = await driveService.RetryFailedAsync(token.UserId) });
    }

    [ValourRoute(HttpVerbs.Get, "api/attachments/cleanup/places")]
    [UserRequired]
    public static async Task<IResult> GetCleanupPlacesAsync(
        TokenService tokenService,
        UserStorageService storageService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(await storageService.GetPlacesAsync(token.UserId));
    }

    [ValourRoute(HttpVerbs.Post, "api/attachments/cleanup/preview")]
    [UserRequired]
    public static async Task<IResult> PreviewCleanupAsync(
        [FromBody] AttachmentCleanupCriteria? criteria,
        TokenService tokenService,
        UserStorageService storageService)
    {
        if (criteria is null)
            return ValourResult.BadRequest("Include the cleanup criteria.");

        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(await storageService.PreviewAsync(token.UserId, criteria));
    }

    [ValourRoute(HttpVerbs.Post, "api/attachments/cleanup/archive")]
    [UserRequired]
    public static async Task<IResult> StartCleanupArchiveAsync(
        [FromBody] AttachmentArchiveRequest? request,
        TokenService tokenService,
        UserStorageService storageService)
    {
        if (request?.Criteria is null)
            return ValourResult.BadRequest("Include the cleanup criteria.");

        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(await storageService.CreateArchiveTicketAsync(token.UserId, request.Criteria));
    }

    [ValourRoute(HttpVerbs.Get, "api/attachments/cleanup/archive/{id}/status")]
    [UserRequired]
    public static async Task<IResult> GetCleanupArchiveStatusAsync(
        string id,
        TokenService tokenService,
        UserStorageService storageService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(storageService.GetArchiveStatus(token.UserId, id));
    }

    /// <summary>
    /// Downloads the archive. The unguessable id from <c>cleanup/archive</c> is
    /// the credential, because a browser download cannot carry a token header.
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/attachments/cleanup/archive/{id}")]
    public static async Task<IResult> DownloadCleanupArchiveAsync(
        string id,
        HttpContext ctx,
        UserStorageService storageService)
    {
        ctx.Response.ContentType = "application/zip";
        ctx.Response.Headers.ContentDisposition =
            $"attachment; filename=\"valour-attachments-{DateTime.UtcNow:yyyy-MM-dd}.zip\"";
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();

        var result = await storageService.StreamArchiveAsync(id, ctx.Response.Body, ctx.RequestAborted);

        // A failure before any bytes were written can still be reported properly.
        if (!result.Success && !ctx.Response.HasStarted)
        {
            ctx.Response.Headers.Remove("Content-Disposition");
            return result.Code == 404
                ? ValourResult.NotFound(result.Message)
                : ValourResult.BadRequest(result.Message);
        }

        return Results.Empty;
    }

    [ValourRoute(HttpVerbs.Post, "api/attachments/cleanup/delete")]
    [UserRequired]
    public static async Task<IResult> DeleteCleanupBatchAsync(
        [FromBody] AttachmentCleanupDeleteRequest? request,
        TokenService tokenService,
        UserStorageService storageService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include the cleanup request.");

        var token = await tokenService.GetCurrentTokenAsync();
        var result = await storageService.DeleteBatchAsync(token.UserId, request);

        if (!result.Success)
            return result.Code == 409
                ? ValourResult.Forbid(result.Message)
                : ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }
}
