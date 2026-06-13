using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models.Threads;
using Valour.Shared.Queries;

namespace Valour.Server.Api.Dynamic;

public class ThreadApi
{
    //////////////
    // Threads //
    //////////////

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/threads/{threadId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetThreadAsync(
        long planetId,
        long threadId,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var thread = await threadService.GetThreadAsync(threadId);
        if (thread is null || thread.PlanetId != planetId)
            return ValourResult.NotFound("Thread not found.");

        return Results.Json(thread);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PostThreadAsync(
        long planetId,
        [FromBody] PlanetThread thread,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        if (thread is null)
            return ValourResult.BadRequest("Include thread in body.");

        if (thread.PlanetId != planetId)
            return ValourResult.BadRequest("Thread planet id does not match route planet id.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.PostThreads))
            return ValourResult.LacksPermission(PlanetPermissions.PostThreads);

        var result = await threadService.CreateThreadAsync(thread, member);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Created(ISharedPlanetThread.GetIdRoute(planetId, result.Data.Id), result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/threads/{threadId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PutThreadAsync(
        long planetId,
        long threadId,
        [FromBody] PlanetThread thread,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        if (thread is null)
            return ValourResult.BadRequest("Include thread in body.");

        if (thread.Id != threadId || thread.PlanetId != planetId)
            return ValourResult.BadRequest("Thread id in body does not match route.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var existing = await threadService.GetThreadAsync(threadId);
        if (existing is null || existing.PlanetId != planetId)
            return ValourResult.NotFound("Thread not found.");

        var user = await userService.GetCurrentUserAsync();
        if (existing.AuthorUserId != user.Id)
            return ValourResult.Forbid("Only the author can edit a thread.");

        var result = await threadService.EditThreadAsync(thread);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/threads/{threadId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> DeleteThreadAsync(
        long planetId,
        long threadId,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var existing = await threadService.GetThreadAsync(threadId);
        if (existing is null || existing.PlanetId != planetId)
            return ValourResult.NotFound("Thread not found.");

        var user = await userService.GetCurrentUserAsync();
        var isModeration = existing.AuthorUserId != user.Id;
        if (isModeration)
        {
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageThreads))
                return ValourResult.LacksPermission(PlanetPermissions.ManageThreads);
        }

        var result = await threadService.DeleteThreadAsync(planetId, threadId, user.Id, isModeration);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads/query")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> QueryThreadsAsync(
        long planetId,
        [FromBody] QueryRequest queryRequest,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        if (queryRequest is null)
            return ValourResult.BadRequest("Include query in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var threads = await threadService.QueryPlanetThreadsAsync(planetId, queryRequest);
        return Results.Json(threads);
    }

    [ValourRoute(HttpVerbs.Post, "api/threads/feed")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> QueryFeedAsync(
        [FromBody] QueryRequest queryRequest,
        UserService userService,
        ThreadService threadService)
    {
        if (queryRequest is null)
            return ValourResult.BadRequest("Include query in body.");

        var user = await userService.GetCurrentUserAsync();

        var threads = await threadService.QueryFeedAsync(user.Id, queryRequest);
        return Results.Json(threads);
    }

    ///////////
    // Boosts //
    ///////////

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads/{threadId}/boost")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> BoostThreadAsync(
        long planetId,
        long threadId,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var user = await userService.GetCurrentUserAsync();

        var result = await threadService.SetThreadBoostAsync(planetId, threadId, user.Id, true);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/threads/{threadId}/boost")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> UnboostThreadAsync(
        long planetId,
        long threadId,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var user = await userService.GetCurrentUserAsync();

        var result = await threadService.SetThreadBoostAsync(planetId, threadId, user.Id, false);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads/boosts/lookup")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> LookupThreadBoostsAsync(
        long planetId,
        [FromBody] BoostLookupRequest request,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        if (request?.Ids is null)
            return ValourResult.BadRequest("Include ids in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var user = await userService.GetCurrentUserAsync();

        var boosted = await threadService.GetBoostedThreadIdsAsync(user.Id, request.Ids);
        return Results.Json(boosted);
    }

    [ValourRoute(HttpVerbs.Post, "api/threads/boosts/lookup")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> LookupFeedBoostsAsync(
        [FromBody] BoostLookupRequest request,
        UserService userService,
        ThreadService threadService)
    {
        if (request?.Ids is null)
            return ValourResult.BadRequest("Include ids in body.");

        var user = await userService.GetCurrentUserAsync();

        var boosted = await threadService.GetBoostedThreadIdsAsync(user.Id, request.Ids);
        return Results.Json(boosted);
    }

    /////////////////
    // Moderation //
    /////////////////

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads/{threadId}/lock")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> SetThreadLockAsync(
        long planetId,
        long threadId,
        [FromBody] bool value,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageThreads))
            return ValourResult.LacksPermission(PlanetPermissions.ManageThreads);

        var result = await threadService.SetLockedAsync(planetId, threadId, value, member.UserId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads/{threadId}/pin")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> SetThreadPinAsync(
        long planetId,
        long threadId,
        [FromBody] bool value,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageThreads))
            return ValourResult.LacksPermission(PlanetPermissions.ManageThreads);

        var result = await threadService.SetPinnedAsync(planetId, threadId, value, member.UserId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    ///////////////
    // Comments //
    ///////////////

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/threads/{threadId}/comments/{commentId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetCommentAsync(
        long planetId,
        long threadId,
        long commentId,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var comment = await threadService.GetCommentAsync(commentId);
        if (comment is null || comment.ThreadId != threadId || comment.PlanetId != planetId)
            return ValourResult.NotFound("Comment not found.");

        return Results.Json(comment);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads/{threadId}/comments/query")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> QueryCommentsAsync(
        long planetId,
        long threadId,
        [FromBody] QueryRequest queryRequest,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        if (queryRequest is null)
            return ValourResult.BadRequest("Include query in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var thread = await threadService.GetThreadAsync(threadId);
        if (thread is null || thread.PlanetId != planetId)
            return ValourResult.NotFound("Thread not found.");

        var comments = await threadService.QueryCommentsAsync(threadId, queryRequest);
        return Results.Json(comments);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads/{threadId}/comments")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PostCommentAsync(
        long planetId,
        long threadId,
        [FromBody] ThreadComment comment,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        if (comment is null)
            return ValourResult.BadRequest("Include comment in body.");

        if (comment.ThreadId != threadId || comment.PlanetId != planetId)
            return ValourResult.BadRequest("Comment thread id does not match route.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.CommentOnThreads))
            return ValourResult.LacksPermission(PlanetPermissions.CommentOnThreads);

        var result = await threadService.CreateCommentAsync(comment, member);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Created(
            ISharedThreadComment.GetIdRoute(planetId, threadId, result.Data.Id),
            result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/threads/{threadId}/comments/{commentId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PutCommentAsync(
        long planetId,
        long threadId,
        long commentId,
        [FromBody] ThreadComment comment,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        if (comment is null)
            return ValourResult.BadRequest("Include comment in body.");

        if (comment.Id != commentId || comment.ThreadId != threadId || comment.PlanetId != planetId)
            return ValourResult.BadRequest("Comment id in body does not match route.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var existing = await threadService.GetCommentAsync(commentId);
        if (existing is null || existing.ThreadId != threadId)
            return ValourResult.NotFound("Comment not found.");

        var user = await userService.GetCurrentUserAsync();
        if (existing.AuthorUserId != user.Id)
            return ValourResult.Forbid("Only the author can edit a comment.");

        var result = await threadService.EditCommentAsync(comment);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/threads/{threadId}/comments/{commentId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> DeleteCommentAsync(
        long planetId,
        long threadId,
        long commentId,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var existing = await threadService.GetCommentAsync(commentId);
        if (existing is null || existing.ThreadId != threadId || existing.PlanetId != planetId)
            return ValourResult.NotFound("Comment not found.");

        var user = await userService.GetCurrentUserAsync();
        var isModeration = existing.AuthorUserId != user.Id;
        if (isModeration)
        {
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageThreads))
                return ValourResult.LacksPermission(PlanetPermissions.ManageThreads);
        }

        var result = await threadService.DeleteCommentAsync(planetId, commentId, user.Id, isModeration);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads/{threadId}/comments/{commentId}/boost")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> BoostCommentAsync(
        long planetId,
        long threadId,
        long commentId,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var user = await userService.GetCurrentUserAsync();

        var result = await threadService.SetCommentBoostAsync(planetId, commentId, user.Id, true);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/threads/{threadId}/comments/{commentId}/boost")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> UnboostCommentAsync(
        long planetId,
        long threadId,
        long commentId,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var user = await userService.GetCurrentUserAsync();

        var result = await threadService.SetCommentBoostAsync(planetId, commentId, user.Id, false);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/threads/{threadId}/comments/boosts/lookup")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> LookupCommentBoostsAsync(
        long planetId,
        long threadId,
        [FromBody] BoostLookupRequest request,
        UserService userService,
        PlanetMemberService memberService,
        ThreadService threadService)
    {
        if (request?.Ids is null)
            return ValourResult.BadRequest("Include ids in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var user = await userService.GetCurrentUserAsync();

        var boosted = await threadService.GetBoostedCommentIdsAsync(user.Id, request.Ids);
        return Results.Json(boosted);
    }
}
