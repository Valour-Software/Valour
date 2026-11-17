using Microsoft.AspNetCore.Mvc;
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
