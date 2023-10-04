using Valour.Shared.Authorization;

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
    
    public static async Task<IResult> HasChannelAccess(ValourDB db, PlanetMemberService memberService, long channelId, string userToken)
    {
        var dbChannel = await db.PlanetVoiceChannels.FindAsync(channelId);
        if (dbChannel is null)
            return ValourResult.NotFound("Channel does not exist");

        var channel = dbChannel.ToModel();
        
        var authToken = await db.AuthTokens.FirstOrDefaultAsync(x => x.Id == userToken);
        if (authToken is null)
            return Results.Json(false);
        
        var member = await memberService.GetByUserAsync(authToken.UserId, dbChannel.PlanetId);
        return Results.Json(await memberService.HasPermissionAsync(member, channel, VoiceChannelPermissions.Join));
    }
}