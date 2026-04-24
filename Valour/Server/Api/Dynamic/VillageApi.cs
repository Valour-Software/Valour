#nullable enable

using Valour.Shared.Authorization;
using Valour.Server.Services.Villages;

namespace Valour.Server.Api.Dynamic;

public class VillageApi
{
    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/village/poc")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetProofOfConceptRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetService planetService,
        UserService userService,
        VillageService villageService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var planet = await planetService.GetAsync(id);
        if (planet is null)
            return ValourResult.NotFound("Planet not found");

        var userId = await userService.GetCurrentUserIdAsync();
        var channels = await planetService.GetAllChannelsAsync(id);
        var scene = villageService.BuildProofOfConceptScene(planet, channels, userId);
        return Results.Json(scene);
    }
}
