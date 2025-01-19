using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.API;

/// <summary>
/// This API is supposed to be called from the voice server, not by clients
/// </summary>
public class VoiceSignallingApi
{
    public static void AddRoutes(WebApplication app)
    {
        app.MapGet("api/voice/hasChannelAccess/{channelId}/{userToken}", HasChannelAccess);
    }
    
    public static async Task<IResult> HasChannelAccess(ValourDb db, PlanetMemberService memberService, long channelId, string userToken)
    {
        var dbChannel = await db.Channels.FindAsync(channelId);
        if (dbChannel is null || dbChannel.ChannelType != ChannelTypeEnum.PlanetVoice)
            return ValourResult.NotFound("Channel does not exist");

        var channel = dbChannel.ToModel();
        
        var authToken = await db.AuthTokens.FirstOrDefaultAsync(x => x.Id == userToken);
        if (authToken is null)
            return Results.Json(false);
        
        var member = await memberService.GetByUserAsync(authToken.UserId, dbChannel.PlanetId!.Value);
        return Results.Json(await memberService.HasPermissionAsync(member, channel, VoiceChannelPermissions.Join));
    }
}