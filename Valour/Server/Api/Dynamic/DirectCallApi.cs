using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class DirectCallApi
{
    [ValourRoute(HttpVerbs.Post, "api/direct-calls")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> StartAsync(
        [FromBody] StartDirectCallRequest request,
        DirectCallService callService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var result = await callService.StartAsync(token.UserId, request);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Get, "api/direct-calls/current")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> CurrentAsync(DirectCallService callService, TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(await callService.GetCurrentAsync(token.UserId));
    }

    [ValourRoute(HttpVerbs.Get, "api/direct-calls/{callId}")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetAsync(long callId, DirectCallService callService, TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var call = await callService.GetAsync(callId, token.UserId);
        return call is null ? ValourResult.NotFound("Call not found.") : Results.Json(call);
    }

    [ValourRoute(HttpVerbs.Post, "api/direct-calls/{callId}/accept")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static Task<IResult> AcceptAsync(long callId, DirectCallService service, TokenService tokens) =>
        RunAsync(callId, service.AcceptAsync, tokens);

    [ValourRoute(HttpVerbs.Post, "api/direct-calls/{callId}/decline")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static Task<IResult> DeclineAsync(long callId, DirectCallService service, TokenService tokens) =>
        RunAsync(callId, service.DeclineAsync, tokens);

    [ValourRoute(HttpVerbs.Post, "api/direct-calls/{callId}/end")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static Task<IResult> EndAsync(long callId, DirectCallService service, TokenService tokens) =>
        RunAsync(callId, service.EndAsync, tokens);

    [ValourRoute(HttpVerbs.Post, "api/direct-calls/{callId}/leave")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> LeaveAsync(
        long callId,
        [FromQuery] string? sessionId,
        DirectCallService service,
        TokenService tokens)
    {
        var token = await tokens.GetCurrentTokenAsync();
        var result = await service.LeaveAsync(callId, token.UserId, sessionId);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/direct-calls/{callId}/participants")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> AddParticipantsAsync(
        long callId,
        [FromBody] AddDirectCallParticipantsRequest request,
        DirectCallService service,
        TokenService tokens)
    {
        var token = await tokens.GetCurrentTokenAsync();
        var result = await service.AddParticipantsAsync(callId, token.UserId, request);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/direct-calls/{callId}/token")]
    [UserRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> TokenAsync(
        long callId,
        [FromQuery] string? sessionId,
        DirectCallService service,
        TokenService tokens,
        CoreHubService coreHubService)
    {
        var token = await tokens.GetCurrentTokenAsync();
        var result = await service.CreateTokenAsync(callId, token.UserId, sessionId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        coreHubService.NotifyVoiceSessionReplace(token.UserId, new VoiceSessionReplaceEvent
        {
            ChannelId = callId,
            SessionId = sessionId ?? string.Empty
        });
        return Results.Json(result.Data);
    }

    private static async Task<IResult> RunAsync(
        long callId,
        Func<long, long, Task<Valour.Shared.TaskResult<DirectCall>>> action,
        TokenService tokens)
    {
        var token = await tokens.GetCurrentTokenAsync();
        var result = await action(callId, token.UserId);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }
}
