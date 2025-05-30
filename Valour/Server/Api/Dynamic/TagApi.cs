using System.Web.Http;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class TagApi
{
    [ValourRoute(HttpVerbs.Get, "api/tags")]
    [UserRequired(UserPermissionsEnum.View)]
    public static async Task<IResult> GetAllTags (ITagService tagService)
    {
        var tagList = await tagService.GetAllTagsList();
        return tagList.Count>=1 ? Results.Ok(tagList) : Results.NotFound();
    }
    
    [ValourRoute(HttpVerbs.Post, "api/tags")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> CreateAsync (
        ITagService tagService,
        [FromBody] PlanetTag planetTag)
    {
        if(planetTag == null)
            return ValourResult.BadRequest("The tag cannot be null.");
        
        var response = await tagService.CreateAsync(planetTag);

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