using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class ReactionApi
{
    [ValourRoute(HttpVerbs.Get, "api/reactions/{reactionId}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetReactionRoute(
        TokenService tokenService,
        ChannelService channelService,
        ReactionService reactionService,
        long reactionId)
    {
        long channelId = 0;

        var token = await tokenService.GetCurrentTokenAsync();
        
        var reaction = await reactionService.GetReaction(reactionId);
        if (reaction is null)
            return ValourResult.NotFound("Reaction not found");

        if (!await channelService.IsMemberAsync(channelId, token.UserId))
        {
            return ValourResult.Forbid("You are not a member of this channel!");
        }
        
        return Results.Json(reaction);
    }
}