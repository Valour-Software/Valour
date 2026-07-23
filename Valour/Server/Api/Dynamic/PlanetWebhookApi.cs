using Microsoft.AspNetCore.Mvc;
using Valour.Server.Utilities;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using WebhookExecuteRequest = Valour.Sdk.Models.WebhookExecuteRequest;
using WebhookMessageEditRequest = Valour.Sdk.Models.WebhookMessageEditRequest;

namespace Valour.Server.Api.Dynamic;

public class PlanetWebhookApi
{
    ////////////////////////////////////////////////////////////////
    // Management routes — require membership + ManageWebhooks
    ////////////////////////////////////////////////////////////////

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/webhooks")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> GetPlanetWebhooksRouteAsync(
        long planetId,
        PlanetMemberService memberService,
        PlanetWebhookService webhookService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageWebhooks))
            return ValourResult.LacksPermission(PlanetPermissions.ManageWebhooks);

        return Results.Json(await webhookService.GetAllForPlanetAsync(planetId));
    }

    [ValourRoute(HttpVerbs.Get, "api/planetwebhooks/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> GetRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetWebhookService webhookService)
    {
        var webhook = await webhookService.GetAsync(id);
        if (webhook is null)
            return ValourResult.NotFound<PlanetWebhook>();

        var member = await memberService.GetCurrentAsync(webhook.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageWebhooks))
            return ValourResult.LacksPermission(PlanetPermissions.ManageWebhooks);

        return Results.Json(webhook);
    }

    [ValourRoute(HttpVerbs.Post, "api/planetwebhooks")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetWebhook webhook,
        PlanetMemberService memberService,
        PlanetWebhookService webhookService)
    {
        if (webhook is null)
            return ValourResult.BadRequest("Include webhook in body.");

        var member = await memberService.GetCurrentAsync(webhook.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageWebhooks))
            return ValourResult.LacksPermission(PlanetPermissions.ManageWebhooks);

        var result = await webhookService.CreateAsync(webhook, member);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        // The only response carrying the token besides get-by-id and rotate
        return Results.Created($"api/planetwebhooks/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/planetwebhooks/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetWebhook webhook,
        long id,
        PlanetMemberService memberService,
        PlanetWebhookService webhookService)
    {
        if (webhook is null)
            return ValourResult.BadRequest("Include webhook in body.");

        if (webhook.Id != id)
            return ValourResult.BadRequest("Route id does not match webhook id.");

        var existing = await webhookService.GetAsync(id);
        if (existing is null)
            return ValourResult.NotFound<PlanetWebhook>();

        var member = await memberService.GetCurrentAsync(existing.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageWebhooks))
            return ValourResult.LacksPermission(PlanetPermissions.ManageWebhooks);

        var result = await webhookService.UpdateAsync(webhook);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planetwebhooks/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetWebhookService webhookService)
    {
        var webhook = await webhookService.GetAsync(id);
        if (webhook is null)
            return ValourResult.NotFound<PlanetWebhook>();

        var member = await memberService.GetCurrentAsync(webhook.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageWebhooks))
            return ValourResult.LacksPermission(PlanetPermissions.ManageWebhooks);

        var result = await webhookService.DeleteAsync(id);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/planetwebhooks/{id}/rotate")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> RotateRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetWebhookService webhookService)
    {
        var webhook = await webhookService.GetAsync(id);
        if (webhook is null)
            return ValourResult.NotFound<PlanetWebhook>();

        var member = await memberService.GetCurrentAsync(webhook.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageWebhooks))
            return ValourResult.LacksPermission(PlanetPermissions.ManageWebhooks);

        var result = await webhookService.RotateTokenAsync(id);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    ////////////////////////////////////////////////////////////////
    // Execute routes — anonymous; the token in the URL is the
    // credential (invite-code pattern), verified fixed-time
    ////////////////////////////////////////////////////////////////

    [ValourRoute(HttpVerbs.Get, "api/webhooks/{id}/{token}")]
    public static async Task<IResult> GetExecuteInfoRouteAsync(
        long id,
        string token,
        PlanetWebhookService webhookService)
    {
        var webhook = await webhookService.AuthenticateAsync(id, token);
        if (webhook is null)
            return ValourResult.Forbid("Unknown webhook or invalid token.");

        // Token holder already knows the token; still avoid echoing it
        return Results.Json(webhook.WithoutToken());
    }

    [ValourRoute(HttpVerbs.Post, "api/webhooks/{id}/{token}")]
    public static async Task<IResult> ExecuteRouteAsync(
        [FromBody] WebhookExecuteRequest request,
        long id,
        string token,
        HttpContext ctx,
        PlanetWebhookService webhookService,
        WebhookRateLimiter rateLimiter)
    {
        var webhook = await webhookService.AuthenticateAsync(id, token);
        if (webhook is null)
            return ValourResult.Forbid("Unknown webhook or invalid token.");

        if (!TryAcquireRate(webhook.Id, ctx, rateLimiter, out var limited))
            return limited;

        var result = await webhookService.ExecuteAsync(webhook, request);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/webhooks/{id}/{token}/messages/{messageId}")]
    public static async Task<IResult> EditMessageRouteAsync(
        [FromBody] WebhookMessageEditRequest request,
        long id,
        string token,
        long messageId,
        HttpContext ctx,
        PlanetWebhookService webhookService,
        WebhookRateLimiter rateLimiter)
    {
        var webhook = await webhookService.AuthenticateAsync(id, token);
        if (webhook is null)
            return ValourResult.Forbid("Unknown webhook or invalid token.");

        if (!TryAcquireRate(webhook.Id, ctx, rateLimiter, out var limited))
            return limited;

        var result = await webhookService.EditMessageAsync(webhook, messageId, request);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/webhooks/{id}/{token}/messages/{messageId}")]
    public static async Task<IResult> DeleteMessageRouteAsync(
        long id,
        string token,
        long messageId,
        HttpContext ctx,
        PlanetWebhookService webhookService,
        WebhookRateLimiter rateLimiter)
    {
        var webhook = await webhookService.AuthenticateAsync(id, token);
        if (webhook is null)
            return ValourResult.Forbid("Unknown webhook or invalid token.");

        if (!TryAcquireRate(webhook.Id, ctx, rateLimiter, out var limited))
            return limited;

        var result = await webhookService.DeleteMessageAsync(webhook, messageId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }

    private static bool TryAcquireRate(long webhookId, HttpContext ctx, WebhookRateLimiter rateLimiter, out IResult limited)
    {
        if (rateLimiter.TryAcquire(webhookId, out var retryAfter))
        {
            limited = null;
            return true;
        }

        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        ctx.Response.Headers.RetryAfter = seconds.ToString();
        limited = Results.StatusCode(StatusCodes.Status429TooManyRequests);
        return false;
    }
}
