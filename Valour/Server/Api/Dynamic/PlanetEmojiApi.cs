using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class PlanetEmojiApi
{
    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/emojis")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAllAsync(
        long planetId,
        PlanetMemberService memberService,
        PlanetEmojiService emojiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var emojis = await emojiService.GetAllAsync(planetId);
        return Results.Json(emojis);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/emojis/{emojiId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAsync(
        long planetId,
        long emojiId,
        PlanetMemberService memberService,
        PlanetEmojiService emojiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var emoji = await emojiService.GetAsync(planetId, emojiId);
        if (emoji is null)
            return ValourResult.NotFound("Emoji not found.");

        return Results.Json(emoji);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/emojis/{emojiId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteAsync(
        long planetId,
        long emojiId,
        PlanetMemberService memberService,
        PlanetEmojiService emojiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var result = await emojiService.DeleteAsync(planetId, emojiId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }
}
