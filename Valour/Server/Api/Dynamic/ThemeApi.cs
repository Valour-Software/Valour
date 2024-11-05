using Microsoft.AspNetCore.Mvc;
using Valour.Server.Models.Themes;

namespace Valour.Server.Api.Dynamic;

public class ThemeApi 
{
    [ValourRoute(HttpVerbs.Get, "api/themes")]
    [UserRequired]
    public static async Task<IResult> GetThemes(
        ThemeService themeService,
        [FromQuery] int take = 20, 
        [FromQuery] int skip = 0, 
        [FromQuery] string search = null)
    {
        var themes = await themeService.GetThemes(page, take, search);
        return Results.Json(themes);
    } 
    
    [ValourRoute(HttpVerbs.Get, "api/themes/me")]
    [UserRequired]
    public static async Task<IResult> GetMyThemes(
        ThemeService themeService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var themes = await themeService.GetThemesByUser(token.UserId);

        return Results.Json(themes);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/themes/{id}")]
    [UserRequired]
    public static async Task<IResult> GetTheme(
        ThemeService themeService,
        [FromRoute] long id)
    {
        var theme = await themeService.GetTheme(id);
        if (theme is null)
            return ValourResult.NotFound("Theme not found");
        
        return Results.Json(theme);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/themes")]
    [UserRequired]
    public static async Task<IResult> CreateTheme(
        TokenService tokenService,
        ThemeService themeService,
        [FromBody] Theme theme)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        theme.AuthorId = token.UserId;
        
        var result = await themeService.CreateTheme(theme);

        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Put, "api/themes/{id}")]
    [UserRequired]
    public static async Task<IResult> UpdateTheme(
        TokenService tokenService,
        ThemeService themeService,
        [FromRoute] long id,
        [FromBody] Theme theme)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        var existingTheme = await themeService.GetTheme(id);
        if (existingTheme is null)
            return ValourResult.NotFound("Theme not found");
        
        if (existingTheme.AuthorId != token.UserId)
            return ValourResult.Forbid("You do not have permission to edit this theme");
        
        theme.Id = id;
        theme.AuthorId = token.UserId;
        
        var result = await themeService.UpdateTheme(theme);

        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/themes/{id}/votes")]
    [UserRequired]
    public static async Task<IResult> GetThemeVotes(
        ThemeService themeService,
        [FromRoute] long id)
    {
        var votes = await themeService.GetThemeVotesAsync(id);
        return Results.Json(votes);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/themes/{id}/votes/self")]
    [UserRequired]
    public static async Task<IResult> GetSelfThemeVote(
        ThemeService themeService,
        TokenService tokenService,
        [FromRoute] long id)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var vote = await themeService.GetUserVote(token.UserId, id);

        return ValourResult.Json(vote);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/themes/{themeId}/votes")]
    [UserRequired]
    public static async Task<IResult> CreateThemeVote(
        ThemeService themeService,
        TokenService tokenService,
        [FromRoute] long themeId,
        [FromBody] ThemeVote vote)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        if (themeId != vote.ThemeId)
            return ValourResult.BadRequest("ThemeId in route does not match ThemeId in body");
        
        if (token.UserId != vote.UserId)
            return ValourResult.Forbid("You do not have permission to vote for another user");

        var result = await themeService.CreateThemeVote(vote);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Put, "api/themes/{themeId}/votes/{voteId}")]
    [UserRequired]
    public static async Task<IResult> UpdateThemeVote(
        ThemeService themeService,
        TokenService tokenService,
        [FromRoute] long themeId,
        [FromRoute] long voteId,
        [FromBody] ThemeVote vote)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        if (themeId != vote.ThemeId)
            return ValourResult.BadRequest("ThemeId in route does not match ThemeId in body");
        
        if (voteId != vote.Id)
            return ValourResult.BadRequest("VoteId in route does not match VoteId in body");
        
        if (token.UserId != vote.UserId)
            return ValourResult.Forbid("You do not have permission to vote for another user");

        // Create also updates
        var result = await themeService.CreateThemeVote(vote);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Delete, "api/themes/{themeId}/votes/{voteId}")]
    [UserRequired]
    public static async Task<IResult> DeleteThemeVote(
        ThemeService themeService,
        TokenService tokenService,
        [FromRoute] long themeId,
        [FromRoute] long voteId)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var vote = await themeService.GetUserVote(token.UserId, themeId);
        if (vote is null || vote.Id != voteId)
            return ValourResult.NotFound("Vote not found or you do not have permission to delete it");
        
        var result = await themeService.DeleteThemeVote(voteId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return ValourResult.Ok();
    }
}