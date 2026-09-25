using System.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class TagApi
{
    /// <summary>
    /// Returns tags with curated tags first, at most <see cref="TagService.MaxTagListSize"/> per request.
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/tags")]
    [UserRequired(UserPermissionsEnum.View)]
    public static async Task<IResult> GetAllTags (
        ITagService tagService,
        int skip = 0,
        int take = TagService.MaxTagListSize)
    {
        var tagList = await tagService.GetAllTagsList(skip, take);
        return tagList.Count>=1 ? Results.Ok(tagList) : Results.NotFound();
    }
    
    [ValourRoute(HttpVerbs.Post, "api/tags")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> CreateAsync (
        ITagService tagService,
        UserService userService,
        [FromBody] PlanetTag planetTag)
    {
        if(planetTag == null)
            return ValourResult.BadRequest("The tag cannot be null.");

        var userId = await userService.GetCurrentUserIdAsync();
        var response = await tagService.CreateAsync(planetTag, userId);

        if (!response.Success)
        {
            return Results.BadRequest(response.Message);
        }
        return Results.Created($"api/tags/{response.Data.Id}", response.Data);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/tags/{tagId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> GetTagById (
        ITagService tagService,
        long tagId)
    {
        var response = await tagService.FindAsync(tagId);
        
        if(!response.Success)
            return Results.NotFound();
        
        return Results.Ok(response.Data);
    }
    
    
    
    
    
}