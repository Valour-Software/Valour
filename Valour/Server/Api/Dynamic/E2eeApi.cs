#nullable enable annotations

using Microsoft.AspNetCore.Mvc;
using Valour.Sdk.E2ee;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// End-to-end encryption routes. Everything here moves public keys, signed
/// records, and sealed boxes; no route receives a key that can read messages.
/// </summary>
public class E2eeApi
{
    // Key logs and user key boxes

    [ValourRoute(HttpVerbs.Get, "api/e2ee/users/{userId}/log")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetKeyLogAsync(long userId, int? known, E2eeIdentityService identity) =>
        Results.Json(await identity.GetLogAsync(userId, Math.Max(0, known ?? 0)));

    [ValourRoute(HttpVerbs.Post, "api/e2ee/users/logs")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetKeyLogsAsync([FromBody] UserKeyLogsRequest? request,
        E2eeIdentityService identity)
    {
        if (request?.KnownCounts is null)
            return ValourResult.BadRequest("Include the users to fetch.");
        if (request.KnownCounts.Count > E2eeLimits.MaxUsersPerKeyLogRequest)
            return ValourResult.BadRequest($"Request at most {E2eeLimits.MaxUsersPerKeyLogRequest} users at a time.");

        return Results.Json(await identity.GetLogsAsync(request.KnownCounts));
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/users/me/log")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [RateLimit(RateLimitPolicies.E2ee)]
    public static async Task<IResult> AppendKeyLogAsync([FromBody] AppendKeyLogRequest? request,
        UserService userService, E2eeIdentityService identity)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        // Devices are added through link approval so both devices confirm the
        // keys; only a recovery key may add a device directly.
        var result = await identity.AppendAsync(userId, request?.Entry, request?.Boxes, allowAddDevice: false);
        return result.Success ? Results.Json(request!.Entry) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Get, "api/e2ee/users/me/boxes/{recipientId}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [RateLimit(RateLimitPolicies.Auth)]
    public static async Task<IResult> GetUserKeyBoxAsync(string recipientId, int? generation, UserService userService,
        E2eeIdentityService identity)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var box = await identity.GetBoxAsync(userId, recipientId, generation);
        return box is null ? ValourResult.NotFound("Key box not found.") : Results.Json(box);
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/users/me/boxes")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [RateLimit(RateLimitPolicies.E2ee)]
    public static async Task<IResult> StoreUserKeyBoxesAsync([FromBody] List<UserKeyBoxDto>? boxes,
        UserService userService, E2eeIdentityService identity)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await identity.StoreBoxesAsync(userId, boxes ?? []);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }

    // Device linking

    [ValourRoute(HttpVerbs.Post, "api/e2ee/link-sessions")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [RateLimit(RateLimitPolicies.Auth)]
    public static async Task<IResult> CreateLinkSessionAsync([FromBody] CreateDeviceLinkRequest? request,
        UserService userService, E2eeIdentityService identity)
    {
        if (request is null)
            return ValourResult.BadRequest("Include the link request.");

        var userId = await userService.GetCurrentUserIdAsync();
        var result = await identity.CreateLinkSessionAsync(userId, request);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Get, "api/e2ee/link-sessions/pending")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetPendingLinkSessionsAsync(UserService userService, E2eeIdentityService identity)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await identity.GetPendingLinkSessionsAsync(userId));
    }

    [ValourRoute(HttpVerbs.Get, "api/e2ee/link-sessions/{sessionId}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetLinkSessionAsync(string sessionId, UserService userService,
        E2eeIdentityService identity)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var session = await identity.GetLinkSessionAsync(userId, sessionId);
        return session is null ? ValourResult.NotFound("Link request not found.") : Results.Json(session);
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/link-sessions/{sessionId}/join")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [RateLimit(RateLimitPolicies.Auth)]
    public static async Task<IResult> JoinLinkSessionAsync(string sessionId, [FromBody] JoinDeviceLinkRequest? request,
        UserService userService, E2eeIdentityService identity)
    {
        if (request is null)
            return ValourResult.BadRequest("Include the new device's keys.");

        var userId = await userService.GetCurrentUserIdAsync();
        var result = await identity.JoinLinkSessionAsync(userId, sessionId, request);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/link-sessions/{sessionId}/approve")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ApproveLinkSessionAsync(string sessionId,
        [FromBody] ApproveDeviceLinkRequest? request, UserService userService, E2eeIdentityService identity)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await identity.ApproveLinkSessionAsync(userId, sessionId, request);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/link-sessions/{sessionId}/deny")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DenyLinkSessionAsync(string sessionId, UserService userService,
        E2eeIdentityService identity)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await identity.DenyLinkSessionAsync(userId, sessionId);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }

    // Server attestation keys

    [ValourRoute(HttpVerbs.Get, "api/e2ee/server-keys")]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetServerKeysAsync(E2eeServerKeyService serverKeys)
    {
        var keys = await serverKeys.GetPublicKeysAsync();
        return Results.Json(keys.Select(k => new E2eeServerKeyDto { KeyId = k.Key, PublicKey = k.Value }).ToList());
    }

    // Channel keys

    /// <summary>
    /// Loads a channel the caller may use for encryption, along with the
    /// caller's token, or returns the error to send instead.
    /// </summary>
    private static async Task<(Channel? Channel, AuthToken? Token, IResult? Error)> GetAccessibleChannelAsync(
        long channelId, long? planetId, ChannelService channelService, TokenService tokenService,
        E2eeChannelKeyService channelKeys)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var channel = await channelService.GetChannelAsync(planetId, channelId);
        if (channel is null || !E2eeChannelKeyService.SupportsEncryption(channel))
            return (null, null, ValourResult.NotFound("Channel not found."));

        if (channel.PlanetId is null && !token.HasScope(UserPermissions.DirectMessages))
            return (null, null, ValourResult.Forbid("Token lacks permission for direct messages."));

        if (!await channelKeys.CanViewAsync(channel, token.UserId))
            return (null, null, ValourResult.Forbid("You cannot view this channel."));

        return (channel, token, null);
    }

    [ValourRoute(HttpVerbs.Get, "api/e2ee/channels/{channelId}/keys")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetChannelKeysAsync(long channelId, long? planetId, ChannelService channelService,
        TokenService tokenService, E2eeChannelKeyService channelKeys)
    {
        var (channel, token, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        return Results.Json(await channelKeys.GetStateAsync(channel!, token!.UserId));
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/channels/{channelId}/generations")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2ee)]
    public static async Task<IResult> CreateChannelKeyGenerationAsync(long channelId, long? planetId,
        [FromBody] CreateChannelKeyGenerationRequest? request, ChannelService channelService, TokenService tokenService,
        E2eeChannelKeyService channelKeys)
    {
        var (channel, token, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        var result = await channelKeys.CreateGenerationAsync(channel!, token!.UserId, request!);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/channels/{channelId}/boxes")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2ee)]
    public static async Task<IResult> ShareChannelKeyBoxesAsync(long channelId, long? planetId,
        [FromBody] List<ChannelKeyBoxDto>? boxes, ChannelService channelService, TokenService tokenService,
        E2eeChannelKeyService channelKeys)
    {
        var (channel, token, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        var result = await channelKeys.ShareBoxesAsync(channel!, token!.UserId, boxes ?? []);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    /// <summary>
    /// Removes the caller's boxes for generations from <paramref name="floor"/>
    /// that their box for <paramref name="through"/> unlocks. Devices call this
    /// after opening that part of the chain themselves.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/e2ee/channels/{channelId}/boxes/prune")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> PruneOwnChannelKeyBoxesAsync(long channelId, long? planetId, int through,
        int floor, ChannelService channelService, TokenService tokenService, E2eeChannelKeyService channelKeys)
    {
        var (channel, token, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        await channelKeys.PruneOwnBoxesAsync(channel!.Id, token!.UserId, through, floor);
        return Results.Ok();
    }

    /// <summary>
    /// Removes the caller's box for a generation that their device could not
    /// open, so another member can share a working one.
    /// </summary>
    [ValourRoute(HttpVerbs.Delete, "api/e2ee/channels/{channelId}/boxes/{generation}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> DeleteOwnChannelKeyBoxAsync(long channelId, long? planetId, int generation,
        ChannelService channelService, TokenService tokenService, E2eeChannelKeyService channelKeys)
    {
        var (channel, token, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        await channelKeys.DeleteOwnBoxAsync(channel!.Id, token!.UserId, generation);
        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Get, "api/e2ee/channels/{channelId}/recipients")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetChannelKeyRecipientsAsync(long channelId, long? planetId,
        ChannelService channelService, TokenService tokenService, E2eeChannelKeyService channelKeys)
    {
        var (channel, _, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        return Results.Json(await channelKeys.GetRecipientsAsync(channel!));
    }

    /// <summary>
    /// Signed generation records in a range. Members load these when they read
    /// messages older than the generations they received with the channel's keys.
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/e2ee/channels/{channelId}/generations")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetChannelKeyGenerationsAsync(long channelId, long? planetId, int from, int to,
        ChannelService channelService, TokenService tokenService, E2eeChannelKeyService channelKeys)
    {
        var (channel, _, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        return Results.Json(await channelKeys.GetGenerationsAsync(channel!, Math.Max(1, from), to));
    }

    /// <summary>
    /// Members a new key should be sealed to right away, most recently active
    /// first.
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/e2ee/channels/{channelId}/key-candidates")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2ee)]
    public static async Task<IResult> GetChannelKeyCandidatesAsync(long channelId, long? planetId, int? limit,
        ChannelService channelService, TokenService tokenService, E2eeChannelKeyService channelKeys)
    {
        var (channel, token, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        return Results.Json(await channelKeys.GetKeyCandidatesAsync(channel!, token!.UserId,
            limit ?? E2eeLimits.MaxKeyCandidates));
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/channels/{channelId}/key-requests")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2ee)]
    public static async Task<IResult> RequestChannelKeysAsync(long channelId, long? planetId,
        ChannelService channelService, TokenService tokenService, E2eeChannelKeyService channelKeys)
    {
        var (channel, token, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        var result = await channelKeys.RequestKeysAsync(channel!, token!.UserId);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Get, "api/e2ee/key-requests")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetDirectKeyRequestsAsync(UserService userService, E2eeChannelKeyService channelKeys)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await channelKeys.GetDirectRequestsForHolderAsync(userId));
    }

    // Encrypted search and sealed-history indexing

    [ValourRoute(HttpVerbs.Post, "api/e2ee/channels/{channelId}/search")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2ee)]
    public static async Task<IResult> SearchEncryptedAsync(long channelId, long? planetId,
        [FromBody] EncryptedSearchRequest? request, ChannelService channelService, TokenService tokenService,
        E2eeChannelKeyService channelKeys, E2eeMessageService messages, UserBlockService blocks)
    {
        var (channel, token, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;
        if (request is null)
            return ValourResult.BadRequest("Include the search terms.");

        var results = await messages.SearchAsync(channel!, request);
        var hidden = await blocks.GetEffectiveHiddenUserIdsAsync(token!.UserId);
        if (hidden.Count == 0)
            return Results.Json(results);

        // Messages from blocked users are left out, and so are the previews
        // of their messages that other results reply to, as in message history.
        results = ChannelApi.RemoveBlockedReplyPreviews(results.Where(m => !hidden.Contains(m.AuthorUserId)), hidden);

        return Results.Json(results);
    }

    [ValourRoute(HttpVerbs.Get, "api/e2ee/channels/{channelId}/unindexed")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetUnindexedAsync(long channelId, long? planetId, int? count,
        ChannelService channelService, TokenService tokenService, E2eeChannelKeyService channelKeys,
        E2eeMessageService messages)
    {
        if (count is < 1 or > E2eeLimits.MaxUnindexedBatch)
            return ValourResult.BadRequest($"Count must be between 1 and {E2eeLimits.MaxUnindexedBatch}.");

        var (channel, _, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        return Results.Json(await messages.GetUnindexedAsync(channel!, count ?? E2eeLimits.MaxUnindexedBatch));
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/channels/{channelId}/terms")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> SetSealedTermsAsync(long channelId, long? planetId,
        [FromBody] List<MessageTermsDto>? items, ChannelService channelService, TokenService tokenService,
        E2eeChannelKeyService channelKeys, E2eeMessageService messages)
    {
        var (channel, _, error) = await GetAccessibleChannelAsync(channelId, planetId, channelService, tokenService, channelKeys);
        if (error is not null)
            return error;

        var result = await messages.SetSealedTermsAsync(channel!, items);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }

    // Access logs

    [ValourRoute(HttpVerbs.Get, "api/e2ee/access-logs/{scope}/{scopeId}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetAccessLogAsync(int scope, long scopeId, int? known, bool? full,
        UserService userService, PlanetMemberService memberService, ValourDb db, E2eeAccessLogService accessLogs)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        if (!await CanReadAccessLogAsync((AccessLogScope)scope, scopeId, userId, memberService, db))
            return ValourResult.Forbid("You cannot view this membership log.");

        // A device with no entries starts from the latest checkpoint unless it
        // asks for the whole log.
        return Results.Json(await accessLogs.GetEntriesAsync((AccessLogScope)scope, scopeId, Math.Max(0, known ?? 0),
            full ?? false, E2eeAccessLogService.MaxResponseBytes));
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/access-logs/{scope}/{scopeId}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    [RateLimit(RateLimitPolicies.E2ee)]
    public static async Task<IResult> AppendAccessLogAsync(int scope, long scopeId, [FromBody] AccessLogEntry? entry,
        UserService userService, E2eeAccessLogService accessLogs)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        // Group DMs start their log here. Planets start theirs when the owner
        // makes the planet private.
        var result = await accessLogs.AppendAsync((AccessLogScope)scope, scopeId, userId, entry,
            allowStart: (AccessLogScope)scope == AccessLogScope.GroupChannel);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }

    private static async Task<bool> CanReadAccessLogAsync(AccessLogScope scope, long scopeId, long userId,
        PlanetMemberService memberService, ValourDb db)
    {
        return scope switch
        {
            AccessLogScope.Planet => await memberService.GetCurrentAsync(scopeId) is not null,
            AccessLogScope.GroupChannel => await db.ChannelMembers.AnyAsync(x => x.ChannelId == scopeId && x.UserId == userId),
            _ => false
        };
    }

    // Planets

    [ValourRoute(HttpVerbs.Get, "api/e2ee/planets/{planetId}/member-ids")]
    [UserRequired(UserPermissionsEnum.Membership)]
    [RateLimit(RateLimitPolicies.E2ee)]
    public static async Task<IResult> GetPlanetMemberIdsAsync(long planetId, PlanetMemberService memberService,
        HostedPlanetService hostedPlanetService)
    {
        if (await memberService.GetCurrentAsync(planetId) is null)
            return ValourResult.Forbid("You are not a member of this planet.");

        var hosted = await hostedPlanetService.GetRequiredAsync(planetId);
        return Results.Json(hosted.GetMemberUserIds());
    }

    [ValourRoute(HttpVerbs.Get, "api/e2ee/planets/{planetId}/automod/work")]
    [UserRequired(UserPermissionsEnum.Membership)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> GetAutomodWorkAsync(long planetId, PlanetMemberService memberService,
        E2eeAutomodService automod)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.Forbid("You are not a member of this planet.");

        var result = await automod.GetWorkAsync(planetId, member);
        return result.Success ? Results.Json(result.Data) : ValourResult.Forbid(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/e2ee/planets/{planetId}/automod/terms")]
    [UserRequired(UserPermissionsEnum.Membership)]
    [RateLimit(RateLimitPolicies.E2eeRead)]
    public static async Task<IResult> SaveAutomodTermsAsync(long planetId, [FromBody] List<AutomodTermsUploadDto>? uploads,
        PlanetMemberService memberService, E2eeAutomodService automod)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.Forbid("You are not a member of this planet.");

        var result = await automod.SaveAsync(planetId, member, uploads ?? []);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }
}
