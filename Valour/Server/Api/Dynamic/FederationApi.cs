using Microsoft.AspNetCore.Mvc;
using Valour.Config.Configs;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// Federation endpoints. Hub role: JWKS well-known, node registry, token
/// minting. Node role: challenge well-known, token exchange.
/// </summary>
public class FederationApi
{
    // ================= Hub =================

    [ValourRoute(HttpVerbs.Get, "/.well-known/valour-federation")]
    public static async Task<IResult> GetJwksRoute(FederationKeyService keyService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var jwks = await keyService.GetJwksJsonAsync();
        return Results.Content(jwks, "application/json");
    }

    [ValourRoute(HttpVerbs.Post, "api/federation/nodes")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> RegisterNodeRoute(
        [FromBody] FederatedNodeRegistrationRequest request,
        FederationHubService hubService,
        UserService userService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var user = await userService.GetCurrentUserAsync();

        var result = await hubService.RegisterNodeAsync(user.Id, request?.Domain);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/federation/nodes/{domain}/verify")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> VerifyNodeRoute(
        string domain,
        FederationHubService hubService,
        UserService userService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var user = await userService.GetCurrentUserAsync();

        var result = await hubService.VerifyNodeAsync(user.Id, domain);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    /// <summary>Staff: suspend a federated node (trust &amp; safety).</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/nodes/{domain}/suspend")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [StaffRequired]
    public static async Task<IResult> SuspendNodeRoute(string domain, FederationHubService hubService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var result = await hubService.SetNodeSuspendedAsync(domain, true);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Node suspended.");
    }

    /// <summary>Staff: reinstate a suspended federated node.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/nodes/{domain}/unsuspend")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [StaffRequired]
    public static async Task<IResult> UnsuspendNodeRoute(string domain, FederationHubService hubService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var result = await hubService.SetNodeSuspendedAsync(domain, false);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Node reinstated.");
    }

    [ValourRoute(HttpVerbs.Get, "api/federation/nodes/{domain}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetNodeRoute(
        string domain,
        FederationHubService hubService,
        UserService userService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var user = await userService.GetCurrentUserAsync();

        var status = await hubService.GetNodeStatusAsync(user.Id, domain);
        if (status is null)
            return ValourResult.NotFound("Node not found.");

        return Results.Json(status);
    }

    [ValourRoute(HttpVerbs.Get, "api/federation/nodes")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetNodesRoute(
        FederationHubService hubService,
        UserService userService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        return Results.Json(await hubService.GetNodesAsync(await userService.GetCurrentUserIdAsync()));
    }

    /// <summary>
    /// Node owner: approve one owner (PlanetId = 0) or one specific official
    /// planet for a forward migration to this node.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/nodes/{domain}/migration-approvals")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> CreateMigrationHostingApprovalRoute(
        string domain,
        [FromBody] FederatedMigrationHostingApprovalRequest request,
        FederationHubService hubService,
        UserService userService)
    {
        var result = await hubService.CreateMigrationHostingApprovalAsync(
            await userService.GetCurrentUserIdAsync(), domain, request);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Get, "api/federation/nodes/{domain}/migration-approvals")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetMigrationHostingApprovalsRoute(
        string domain,
        FederationHubService hubService,
        UserService userService)
    {
        var result = await hubService.GetMigrationHostingApprovalsAsync(
            await userService.GetCurrentUserIdAsync(), domain);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Delete, "api/federation/nodes/{domain}/migration-approvals/{ownerId}/{planetId}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DeleteMigrationHostingApprovalRoute(
        string domain,
        long ownerId,
        long planetId,
        FederationHubService hubService,
        UserService userService)
    {
        var result = await hubService.DeleteMigrationHostingApprovalAsync(
            await userService.GetCurrentUserIdAsync(), domain, ownerId, planetId);
        return result.Success ? ValourResult.Ok("Migration hosting approval revoked.") : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/federation/token")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> MintTokenRoute(
        [FromBody] FederationTokenRequest request,
        FederationHubService hubService,
        UserService userService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var user = await userService.GetCurrentUserAsync();

        var result = await hubService.MintTokenAsync(user, request?.Domain);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    /// <summary>
    /// A client asks the hub for a short-lived identity passport bound to a
    /// public signing key. It is cached client-side for offline invite joins.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/passport")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> MintPassportRoute(
        [FromBody] FederationPassportRequest request,
        FederationInviteService inviteService,
        UserService userService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var user = await userService.GetCurrentUserAsync();
        var result = await inviteService.MintPassportAsync(user, request?.PublicJwk);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    // ================= Node =================

    [ValourRoute(HttpVerbs.Get, "/.well-known/valour-node")]
    public static async Task<IResult> GetNodeWellKnownRoute(FederationKeyService keyService)
    {
        var config = FederationConfig.Current;
        if (config?.NodeEnabled != true)
            return ValourResult.NotFound("This instance is not a community node.");

        return Results.Json(new FederatedNodeWellKnown
        {
            Domain = config.NodeDomain,
            Challenge = config.NodeChallenge,
            Version = typeof(ISharedUser).Assembly.GetName().Version?.ToString(),
            ProtocolVersion = ValourFederation.ProtocolVersion,
            PublicJwk = await keyService.GetNodePublicJwkAsync(),
            AllowsPublicMigrations = config.AllowPublicMigrations,
        });
    }

    [ValourRoute(HttpVerbs.Post, "api/federation/exchange")]
    public static async Task<IResult> ExchangeRoute(
        [FromBody] FederationExchangeRequest request,
        HttpContext ctx,
        FederationNodeService nodeService)
    {
        var result = await nodeService.ExchangeAsync(
            request?.HubToken,
            ctx.Connection?.RemoteIpAddress?.ToString());

        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    // ============ Hub: planet stub registry (node-authenticated) ============

    /// <summary>
    /// The node's self-signed S2S bearer, carried in a dedicated header so it
    /// never collides with user-token handling.
    /// </summary>
    public const string NodeAuthHeader = "X-Valour-Node-Token";

    [ValourRoute(HttpVerbs.Post, "api/federation/planets")]
    public static async Task<IResult> ReservePlanetRoute(
        [FromBody] FederatedPlanetStubRequest request,
        HttpContext ctx,
        FederationHubService hubService,
        FederationPlanetRegistryService registry)
    {
        var domain = await AuthNodeAsync(ctx, hubService);
        if (domain is null)
            return ValourResult.Forbid("Invalid or missing node credentials.");

        var result = await registry.ReserveAsync(domain, request);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/federation/planets/{id}/adopt")]
    public static async Task<IResult> AdoptPlanetRoute(
        long id,
        [FromBody] FederatedPlanetStubRequest request,
        HttpContext ctx,
        FederationHubService hubService,
        FederationPlanetRegistryService registry)
    {
        var domain = await AuthNodeAsync(ctx, hubService);
        if (domain is null)
            return ValourResult.Forbid("Invalid or missing node credentials.");

        var result = await registry.AdoptAsync(domain, id, request);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/federation/planets/{id}")]
    public static async Task<IResult> UpsertPlanetRoute(
        long id,
        [FromBody] FederatedPlanetStubRequest request,
        HttpContext ctx,
        FederationHubService hubService,
        FederationPlanetRegistryService registry)
    {
        var domain = await AuthNodeAsync(ctx, hubService);
        if (domain is null)
            return ValourResult.Forbid("Invalid or missing node credentials.");

        var result = await registry.UpsertAsync(domain, id, request);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/federation/planets/{id}")]
    public static async Task<IResult> DeletePlanetRoute(
        long id,
        HttpContext ctx,
        FederationHubService hubService,
        FederationPlanetRegistryService registry)
    {
        var domain = await AuthNodeAsync(ctx, hubService);
        if (domain is null)
            return ValourResult.Forbid("Invalid or missing node credentials.");

        var result = await registry.DeleteAsync(domain, id);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Deleted.");
    }

    // ============ Offline-verifiable federation invites ============

    /// <summary>Hub owner creates a recipient-bound grant redeemable while the hub is offline.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/invites")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> CreateFederatedInviteRoute(
        [FromBody] FederatedInviteGrantCreateRequest request,
        FederationInviteService inviteService,
        UserService userService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var user = await userService.GetCurrentUserAsync();
        var result = await inviteService.CreateAsync(user.Id, request);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Delete, "api/federation/invites/{grantId}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> RevokeFederatedInviteRoute(
        string grantId,
        FederationInviteService inviteService,
        UserService userService)
    {
        if (!FederationHubService.HubEnabled)
            return ValourResult.NotFound("This instance is not a federation hub.");

        var user = await userService.GetCurrentUserAsync();
        var result = await inviteService.RevokeAsync(user.Id, grantId);
        return result.Success ? ValourResult.Ok("Invite revoked.") : ValourResult.BadRequest(result.Message);
    }

    /// <summary>Destination-node redemption; it validates cached hub keys locally.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/invites/redeem")]
    public static async Task<IResult> RedeemFederatedInviteRoute(
        [FromBody] FederatedInviteRedeemRequest request,
        HttpContext ctx,
        FederationInviteService inviteService)
    {
        var result = await inviteService.RedeemOnNodeAsync(request, ctx.Connection.RemoteIpAddress?.ToString());
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    /// <summary>Hub acknowledgement of a node's queued offline redemption.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/invites/redemptions")]
    public static async Task<IResult> ReportFederatedInviteRedemptionRoute(
        [FromBody] FederatedInviteRedemptionReport report,
        HttpContext ctx,
        FederationHubService hubService,
        FederationInviteService inviteService)
    {
        var domain = await AuthNodeAsync(ctx, hubService);
        if (domain is null)
            return ValourResult.Forbid("Invalid or missing node credentials.");

        var result = await inviteService.ReconcileRedemptionAsync(domain, report);
        return result.Success ? ValourResult.Ok("Redemption acknowledged.") : ValourResult.BadRequest(result.Message);
    }

    // ============ Join-via-hub (accepted domains + membership) ============

    [ValourRoute(HttpVerbs.Get, "api/federation/accepted-domains")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAcceptedDomainsRoute(FederationJoinService join, UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        return Results.Json(await join.GetAcceptedDomainsAsync(user.Id));
    }

    [ValourRoute(HttpVerbs.Post, "api/federation/accepted-domains")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> AcceptDomainRoute(
        [FromBody] AcceptDomainRequest request,
        FederationJoinService join,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        var result = await join.AcceptDomainAsync(user.Id, request?.Domain);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        return ValourResult.Ok("Domain accepted.");
    }

    /// <summary>Resolve where a community-hosted planet lives (and whether its domain is accepted).</summary>
    [ValourRoute(HttpVerbs.Get, "api/federation/planets/{planetId}/location")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> ResolveFederatedPlanetRoute(
        long planetId,
        FederationJoinService join,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        var location = await join.ResolveAsync(user.Id, planetId);
        if (location is null)
            return ValourResult.NotFound("Not a community-hosted planet.");
        return Results.Json(location);
    }

    /// <summary>Join a community-hosted planet (domain must be accepted first).</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/planets/{planetId}/join")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> JoinFederatedPlanetRoute(
        long planetId,
        FederationJoinService join,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        var result = await join.JoinAsync(user.Id, planetId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/federation/planets/{planetId}/leave")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> LeaveFederatedPlanetRoute(
        long planetId,
        FederationJoinService join,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        await join.LeaveAsync(user.Id, planetId);
        return ValourResult.Ok("Left.");
    }

    /// <summary>The user's community-hosted memberships ("your planets on other servers").</summary>
    [ValourRoute(HttpVerbs.Get, "api/federation/memberships")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetFederatedMembershipsRoute(FederationJoinService join, UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        return Results.Json(await join.GetMembershipsAsync(user.Id));
    }

    // ============ Migration ============

    /// <summary>Source (hub): current owner's non-finalized migration states.</summary>
    [ValourRoute(HttpVerbs.Get, "api/federation/migrations")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetOwnerMigrationsRoute(
        FederationMigrationService migration,
        UserService userService)
    {
        var result = await migration.GetOwnerMigrationsAsync(await userService.GetCurrentUserIdAsync());
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    /// <summary>Source (hub): owner authorizes migrating their planet to a node.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/migrations")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> InitiateMigrationRoute(
        [FromBody] MigrationInitiateRequest request,
        FederationMigrationService migration,
        UserService userService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var user = await userService.GetCurrentUserAsync();
        var result = await migration.InitiateAsync(user.Id, request.PlanetId, request.TargetDomain);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    /// <summary>Source (hub): owner cancels a pending migration, unlocking the planet.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/migrations/{planetId}/abort")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> AbortMigrationRoute(
        long planetId,
        FederationMigrationService migration,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        var result = await migration.AbortAsync(user.Id, planetId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Migration aborted; planet unlocked.");
    }

    /// <summary>Source: serves the planet snapshot only to the target node holding the current grant.</summary>
    [ValourRoute(HttpVerbs.Get, "api/federation/migrations/{planetId}/snapshot")]
    public static async Task<IResult> MigrationSnapshotRoute(
        long planetId,
        HttpContext ctx,
        FederationHubService hubService,
        FederationMigrationService migration)
    {
        var domain = await AuthNodeAsync(ctx, hubService);
        if (domain is null)
            return ValourResult.Forbid("Invalid or missing node credentials.");

        var grant = ctx.Request.Headers["X-Valour-Migration-Grant"].ToString();
        var result = await migration.GetSnapshotForGrantAsync(domain, planetId, grant);
        if (!result.Success)
            return ValourResult.Forbid(result.Message);

        return Results.Json(result.Data);
    }

    /// <summary>Source: destination confirms import; source does the handoff. Node-authed.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/migrations/{planetId}/complete")]
    public static async Task<IResult> CompleteMigrationRoute(
        long planetId,
        HttpContext ctx,
        FederationHubService hubService,
        FederationMigrationService migration)
    {
        var domain = await AuthNodeAsync(ctx, hubService);
        if (domain is null)
            return ValourResult.Forbid("Invalid or missing node credentials.");

        var grant = ctx.Request.Headers["X-Valour-Migration-Grant"].ToString();
        var importedHash = ctx.Request.Headers["X-Valour-Snapshot-Hash"].ToString();

        var result = await migration.CompleteAsync(domain, planetId, grant, importedHash);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Handoff recorded. The owner must finalize source deletion after verifying the destination.");
    }

    /// <summary>Source: owner explicitly deletes the locked recovery copy after verifying the destination.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/migrations/{planetId}/finalize")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> FinalizeMigrationRoute(
        long planetId,
        FederationMigrationService migration,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        var result = await migration.FinalizeAsync(user.Id, planetId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Source migration copy deleted.");
    }

    /// <summary>Hub: owner pulls a community-hosted planet back to official.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/migrations/{planetId}/pullback")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> PullBackMigrationRoute(
        long planetId,
        FederationMigrationService migration,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        var result = await migration.PullBackAsync(user.Id, planetId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Planet pulled back to official.");
    }

    /// <summary>Node: serve a snapshot to the hub during a pull-back (hub-grant authed).</summary>
    [ValourRoute(HttpVerbs.Get, "api/federation/migrations/{planetId}/export")]
    public static async Task<IResult> ExportForPullBackRoute(
        long planetId,
        HttpContext ctx,
        FederationMigrationService migration)
    {
        var grant = ctx.Request.Headers["X-Valour-Migration-Grant"].ToString();
        var result = await migration.ExportForPullBackAsync(planetId, grant);
        if (!result.Success)
            return ValourResult.Forbid(result.Message);

        return Results.Json(result.Data);
    }

    /// <summary>Node: delete the local planet after the hub imported it (hub-grant authed).</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/migrations/{planetId}/purge")]
    public static async Task<IResult> PurgeForPullBackRoute(
        long planetId,
        HttpContext ctx,
        FederationMigrationService migration)
    {
        var grant = ctx.Request.Headers["X-Valour-Migration-Grant"].ToString();
        var result = await migration.PurgeForPullBackAsync(planetId, grant);
        if (!result.Success)
            return ValourResult.Forbid(result.Message);

        return ValourResult.Ok("Purged.");
    }

    /// <summary>Node: unfreeze a planet whose pull-back the hub couldn't finish.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/migrations/{planetId}/pullback-abort")]
    public static async Task<IResult> AbortPullBackRoute(
        long planetId,
        HttpContext ctx,
        FederationMigrationService migration)
    {
        var grant = ctx.Request.Headers["X-Valour-Migration-Grant"].ToString();
        var result = await migration.AbortPullBackAsync(planetId, grant);
        if (!result.Success)
            return ValourResult.Forbid(result.Message);

        return ValourResult.Ok("Pull-back aborted; planet unlocked.");
    }

    /// <summary>Destination (node): owner hands over the grant; node pulls + imports.</summary>
    [ValourRoute(HttpVerbs.Post, "api/federation/migrations/import")]
    // A federation-exchanged destination session intentionally has the
    // Membership scope, not node-local FullControl. The signed grant and its
    // owner_id are the authorization for this destructive operation; the
    // current node-local identity must still match that owner.
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> ImportMigrationRoute(
        [FromBody] MigrationImportRequest request,
        FederationMigrationService migration,
        UserService userService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var result = await migration.ImportFromGrantAsync(
            await userService.GetCurrentUserIdAsync(), request.Grant, request.SourceUrl);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Migration imported.");
    }

    /// <summary>Hub: a cursor-paginated, node-scoped account-deletion page. Node-authed.</summary>
    [ValourRoute(HttpVerbs.Get, "api/federation/purges")]
    public static async Task<IResult> GetPurgesRoute(
        [FromQuery] long after,
        HttpContext ctx,
        FederationHubService hubService)
    {
        var domain = await AuthNodeAsync(ctx, hubService);
        if (domain is null)
            return ValourResult.Forbid("Invalid or missing node credentials.");

        var page = await hubService.GetPurgedUserIdsAsync(domain, Math.Max(0, after));
        return Results.Json(page);
    }

    private static async Task<string> AuthNodeAsync(HttpContext ctx, FederationHubService hubService)
    {
        if (!FederationHubService.HubEnabled)
            return null;

        if (!ctx.Request.Headers.TryGetValue(NodeAuthHeader, out var header))
            return null;

        return await hubService.AuthenticateNodeAsync(header.ToString());
    }
}
