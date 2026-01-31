using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class BotApi
{
    /// <summary>
    /// Gets all bots owned by the current user
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/bots")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetMyBotsAsync(
        UserService userService,
        BotService botService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var bots = await botService.GetUserBotsAsync(userId);

        var responses = bots.Select(b => BotResponse.FromUser(b)).ToList();
        return Results.Json(responses);
    }

    /// <summary>
    /// Creates a new bot
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/bots")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> CreateBotAsync(
        [FromBody] CreateBotRequest request,
        UserService userService,
        BotService botService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        if (string.IsNullOrWhiteSpace(request.Name))
            return ValourResult.BadRequest("Bot name is required");

        var userId = await userService.GetCurrentUserIdAsync();
        var result = await botService.CreateBotAsync(userId, request.Name);

        if (!result.Success)
            return ValourResult.Problem(result.Message);

        var (bot, token) = result.Data;
        var response = BotResponse.FromUser(bot, token);
        return Results.Json(response);
    }

    /// <summary>
    /// Gets a specific bot by ID
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/bots/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetBotAsync(
        long id,
        UserService userService,
        BotService botService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        // Verify ownership
        if (!await botService.OwnsBotAsync(userId, id))
            return ValourResult.Forbid("You do not own this bot");

        var bot = await botService.GetBotAsync(id);
        if (bot is null)
            return ValourResult.NotFound("Bot not found");

        var response = BotResponse.FromUser(bot);
        return Results.Json(response);
    }

    /// <summary>
    /// Updates a bot's information
    /// </summary>
    [ValourRoute(HttpVerbs.Put, "api/bots/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> UpdateBotAsync(
        long id,
        [FromBody] UpdateBotRequest request,
        UserService userService,
        BotService botService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        var userId = await userService.GetCurrentUserIdAsync();
        var result = await botService.UpdateBotAsync(id, userId, request);

        if (!result.Success)
            return ValourResult.Problem(result.Message);

        var response = BotResponse.FromUser(result.Data);
        return Results.Json(response);
    }

    /// <summary>
    /// Deletes a bot
    /// </summary>
    [ValourRoute(HttpVerbs.Delete, "api/bots/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DeleteBotAsync(
        long id,
        UserService userService,
        BotService botService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await botService.DeleteBotAsync(id, userId);

        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok("Bot deleted successfully");
    }

    /// <summary>
    /// Regenerates a bot's token
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/bots/{id}/token/regenerate")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> RegenerateBotTokenAsync(
        long id,
        UserService userService,
        BotService botService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await botService.RegenerateBotTokenAsync(id, userId);

        if (!result.Success)
            return ValourResult.Problem(result.Message);

        // Return the new token
        var bot = await botService.GetBotAsync(id);
        var response = BotResponse.FromUser(bot, result.Data);
        return Results.Json(response);
    }
}
