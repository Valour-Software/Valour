using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models.Wiki;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// Member-facing docs API. Reads are open to any planet member; writes are
/// gated by PlanetPermissions.EditWiki (page authoring) and
/// PlanetPermissions.ManageWiki (structure, deletion, revisions restore, and
/// docs settings). The public docs site does not use this API — the public
/// Razor pages call PlanetWikiService directly.
/// </summary>
public class PlanetWikiApi
{
    ///////////
    // Reads //
    ///////////

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/wiki/tree")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetTreeAsync(
        long planetId,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var tree = await wikiService.GetTreeAsync(planetId);
        return Results.Json(tree);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/wiki/{pageId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAsync(
        long planetId,
        long pageId,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var doc = await wikiService.GetAsync(planetId, pageId);
        if (doc is null)
            return ValourResult.NotFound("Doc not found.");

        return Results.Json(doc);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/wiki/by-slug/{slug}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetBySlugAsync(
        long planetId,
        string slug,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var (doc, _) = await wikiService.ResolveSlugAsync(planetId, slug);
        if (doc is null)
            return ValourResult.NotFound("Doc not found.");

        return Results.Json(doc);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/wiki/{pageId}/content")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetContentAsync(
        long planetId,
        long pageId,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var content = await wikiService.GetContentAsync(planetId, pageId);
        if (content is null)
            return ValourResult.NotFound("Doc not found.");

        return Results.Json(content);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/wiki/search")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> SearchAsync(
        long planetId,
        [FromQuery] string q,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var results = await wikiService.SearchAsync(planetId, q, includeUnpublished: true);
        return Results.Json(results);
    }

    ///////////////
    // Mutations //
    ///////////////

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/wiki")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PostAsync(
        long planetId,
        [FromBody] WikiPageCreateRequest request,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        // Folders shape the wiki structure, so they need the manage permission
        var required = request.IsFolder ? PlanetPermissions.ManageWiki : PlanetPermissions.EditWiki;
        if (!await memberService.HasPermissionAsync(member, required))
            return ValourResult.LacksPermission(required);

        var result = await wikiService.CreateAsync(planetId, request, member.UserId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Created(ISharedPlanetWikiPage.GetIdRoute(planetId, result.Data.Id), result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/wiki/{pageId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PutAsync(
        long planetId,
        long pageId,
        [FromBody] PlanetWikiPage doc,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        if (doc is null)
            return ValourResult.BadRequest("Include doc in body.");

        if (doc.Id != pageId)
            return ValourResult.BadRequest("Doc id in body does not match route doc id.");

        if (doc.PlanetId != planetId)
            return ValourResult.BadRequest("Doc planet id does not match route planet id.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var existing = await wikiService.GetAsync(planetId, pageId);
        if (existing is null)
            return ValourResult.NotFound("Doc not found.");

        var required = existing.IsFolder ? PlanetPermissions.ManageWiki : PlanetPermissions.EditWiki;
        if (!await memberService.HasPermissionAsync(member, required))
            return ValourResult.LacksPermission(required);

        var result = await wikiService.UpdateAsync(doc, member.UserId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/wiki/{pageId}/content")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PutContentAsync(
        long planetId,
        long pageId,
        [FromBody] WikiPageContentUpdateRequest request,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.EditWiki))
            return ValourResult.LacksPermission(PlanetPermissions.EditWiki);

        var result = await wikiService.SaveContentAsync(planetId, pageId, request, member.UserId);
        if (!result.Success)
        {
            return result.Code == PlanetWikiService.ConflictErrorCode
                ? Results.Conflict(result.Message)
                : ValourResult.BadRequest(result.Message);
        }

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/wiki/{pageId}/move")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> MoveAsync(
        long planetId,
        long pageId,
        [FromBody] WikiPageMoveRequest request,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageWiki))
            return ValourResult.LacksPermission(PlanetPermissions.ManageWiki);

        var result = await wikiService.MoveAsync(planetId, pageId, request);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/wiki/{pageId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> DeleteAsync(
        long planetId,
        long pageId,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageWiki))
            return ValourResult.LacksPermission(PlanetPermissions.ManageWiki);

        var result = await wikiService.DeleteAsync(planetId, pageId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }

    ///////////////
    // Revisions //
    ///////////////

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/wiki/{pageId}/revisions")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRevisionsAsync(
        long planetId,
        long pageId,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.EditWiki))
            return ValourResult.LacksPermission(PlanetPermissions.EditWiki);

        var revisions = await wikiService.GetRevisionsAsync(planetId, pageId);
        return Results.Json(revisions);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/wiki/{pageId}/revisions/{revisionId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRevisionAsync(
        long planetId,
        long pageId,
        long revisionId,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.EditWiki))
            return ValourResult.LacksPermission(PlanetPermissions.EditWiki);

        var revision = await wikiService.GetRevisionAsync(planetId, pageId, revisionId);
        if (revision is null)
            return ValourResult.NotFound("Revision not found.");

        return Results.Json(revision);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/wiki/{pageId}/revisions/{revisionId}/restore")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> RestoreRevisionAsync(
        long planetId,
        long pageId,
        long revisionId,
        PlanetMemberService memberService,
        PlanetWikiService wikiService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageWiki))
            return ValourResult.LacksPermission(PlanetPermissions.ManageWiki);

        var result = await wikiService.RestoreRevisionAsync(planetId, pageId, revisionId, member.UserId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

}
