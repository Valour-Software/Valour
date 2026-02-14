using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.API;

public class VoiceSignallingApi
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapPost("api/voice/realtimekit/token/{channelId:long}", GetRealtimeKitToken);
    }

    public static async Task<IResult> GetRealtimeKitToken(
        ValourDb db,
        TokenService tokenService,
        PlanetMemberService memberService,
        RealtimeKitService realtimeKitService,
        long channelId)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null)
            return ValourResult.InvalidToken();

        var dbChannel = await db.Channels.FindAsync(channelId);
        if (dbChannel is null || !ISharedChannel.VoiceChannelTypes.Contains(dbChannel.ChannelType))
            return ValourResult.NotFound("Channel does not exist.");

        // Planet voice is the only supported type in this migration pass.
        if (dbChannel.ChannelType != ChannelTypeEnum.PlanetVoice || dbChannel.PlanetId is null)
        {
            return ValourResult.BadRequest("RealtimeKit voice currently supports only planet voice channels.");
        }

        var channel = dbChannel.ToModel();

        var member = await memberService.GetByUserAsync(authToken.UserId, dbChannel.PlanetId.Value);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var hasJoinPermission = await memberService.HasPermissionAsync(member, channel, VoiceChannelPermissions.Join);
        if (!hasJoinPermission)
            return ValourResult.LacksPermission(VoiceChannelPermissions.Join);

        var dbUser = await db.Users.FindAsync(authToken.UserId);
        if (dbUser is null)
            return ValourResult.NotFound("User was not found.");

        var displayName = $"{dbUser.Name}#{dbUser.Tag}";
        TaskResult<RealtimeKitVoiceTokenResponse> tokenResult =
            await realtimeKitService.CreateParticipantTokenAsync(channel, authToken.UserId, displayName);

        if (!tokenResult.Success || tokenResult.Data is null)
        {
            return ValourResult.BadRequest(tokenResult.Message);
        }

        return ValourResult.Json(tokenResult.Data);
    }
}
