using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Valour.Database;
using Valour.Database.Items;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

namespace Valour.Server.API;

/// <summary>
/// The Planet Item API allows for easy construction of routes
/// relating to Valour Planet Items, including permissions handling.
/// </summary>
public class PlanetItemAPI<T> : BaseAPI where T : PlanetItem<T>
{

    /// <summary>
    /// This dummy item is used to expose static methods which
    /// cannot be accessed via a type alone
    /// </summary>
    public T DummyItem = default(T);

    /// <summary>
    /// This method registers the API routes and should only be called
    /// once during the application runtime.
    /// </summary>
    public void RegisterRoutes(WebApplication app)
    {
        T dummy = default(T);
        app.MapGet($"/planets/{{planet_id}}/{dummy.ItemType}/{{id}}", GetRoute);
        app.MapPut($"/planets/{{planet_id}}/{dummy.ItemType}/{{id}}", PutRoute);
        app.MapDelete($"/planets/{{planet_id}}/{dummy.ItemType}/{{id}}", DeleteRoute);
        app.MapPost($"/planets/{{planet_id}}/{dummy.ItemType}", PostRoute);

        // Route for requesting all of a certain item
    }

    // Before you ask why these are seperate and not using just Map(), it's so that
    // Swagger works properly when generating the API route information

    /// <summary>
    /// This returns the requested item for the GET http method
    /// </summary>
    public async Task<IResult> GetRoute
        (HttpContext ctx, ValourDB db, ulong planet_id, ulong id, [FromHeader] string authorization)
            => await BaseRoute(ctx, db, planet_id, id, authorization, Method.GET);

    /// <summary>
    /// This handles PUT requests
    /// </summary>
    public async Task<IResult> PutRoute
        (HttpContext ctx, ValourDB db, ulong planet_id, ulong id, [FromHeader] string authorization)
            => await BaseRoute(ctx, db, planet_id, id, authorization, Method.PUT);

    /// <summary>
    /// This handles DELETE requests
    /// </summary>
    public async Task<IResult> DeleteRoute
        (HttpContext ctx, ValourDB db, ulong planet_id, ulong id, [FromHeader] string authorization)
            => await BaseRoute(ctx, db, planet_id, id, authorization, Method.DELETE);

    /// <summary>
    /// This handles POST requests
    /// </summary>
    public async Task<IResult> PostRoute
        (HttpContext ctx, ValourDB db, ulong planet_id, ulong id, [FromHeader] string authorization)
            => await BaseRoute(ctx, db, planet_id, id, authorization, Method.POST);

    public async Task<IResult> BaseRoute
        (HttpContext ctx, ValourDB db, ulong planet_id, ulong id, [FromHeader] string authorization, Method method)
    {
        var auth = await AuthToken.TryAuthorize(authorization, db);

        // Do not allow any operations which do not have a valid auth token
        if (auth is null)
        {
            await ctx.Response.WriteAsync("Missing authorization header or header is invalid.");
            return Results.Unauthorized();
        }

        // Make sure token has permission to see planet membership
        if (!auth.HasScope(UserPermissions.Membership))
        {
            await ctx.Response.WriteAsync("Missing token scope " + UserPermissions.Membership.Name);
            return Results.Forbid();
        }

        // If it's not a simple GET, require planet management in token scope
        if (method != Method.GET && !auth.HasScope(UserPermissions.PlanetManagement))
        {
            await ctx.Response.WriteAsync("Missing token scope " + UserPermissions.PlanetManagement.Name);
            return Results.Forbid();
        }

        // Get the planet member trying to access the API
        var member = await PlanetMember.FindAsync(auth.User_Id, planet_id, db);

        // If member does not exist, the user cannot have access
        if (member is null)
        {
            await ctx.Response.WriteAsync("Not member of target planet or target planet does not exist.");
            return Results.Forbid();
        }

        // Item reference
        T item = null;

        // Auth steps needed for non-post actions
        if (method != Method.POST)
        {
            // Get the target item
            item = await DummyItem.FindAsync(id, db);

            // Case for the object simply not being found
            if (item is null)
                return Results.NotFound();

            // Check if member has GET permission (needed for all routes)
            var getPerm = await item.CanGetAsync(member, db);

            // Ensure user has GET permission
            if (!getPerm.Success)
            {
                await ctx.Response.WriteAsync(getPerm.Message);
                return Results.Forbid();
            }
        }

        switch (method)
        {
            case Method.GET:
                {
                    return Results.Json(item);
                }
            case Method.PUT:
                {
                    // Read in updated value
                    T updated = await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body);

                    if (updated is null)
                    {
                        await ctx.Response.WriteAsync("Include updated item in body");
                        return Results.BadRequest();
                    }

                    if (item.Id != updated.Id)
                    {
                        await ctx.Response.WriteAsync("Cannot change Id");
                        return Results.BadRequest();
                    }

                    if (item.Planet_Id != planet_id)
                    {
                        await ctx.Response.WriteAsync("Planet_Id does not match");
                        return Results.BadRequest();
                    }

                    // Ensure update is valid and
                    // ensure user has permission to update the item
                    var updatePerm = await updated.CanUpdateAsync(member, item, db);
                    if (!updatePerm.Success)
                    {
                        await ctx.Response.WriteAsync(updatePerm.Message);
                        return Results.Forbid();
                    }

                    await updated.UpdateAsync(db);

                    return Results.NoContent();
                }
            case Method.DELETE:
                {
                    // Ensure user has permission to delete the item
                    var deletePerm = await item.CanDeleteAsync(member, db);
                    if (!deletePerm.Success)
                    {
                        await ctx.Response.WriteAsync(deletePerm.Message);
                        return Results.Forbid();
                    }

                    await item.DeleteAsync(db);

                    return Results.NoContent();
                }
            case Method.POST:
                {
                    // Read in new value
                    T newItem = await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body);
                    if (newItem is null) {
                        await ctx.Response.WriteAsync("Include item in body");
                        return Results.BadRequest();
                    }

                    // Validate new item
                    // We do this *first* because it needs to be valid in order to
                    // determine permissions after
                    var newValid = await newItem.ValidateItemAsync(null, db);
                    if (!newValid.Success) 
                    {
                        await ctx.Response.WriteAsync(newValid.Message);
                        return Results.BadRequest();
                    }

                    var createPerm = await item.CanCreateAsync(member, db);
                    if (!createPerm.Success)
                    {
                        await ctx.Response.WriteAsync(createPerm.Message);
                        return Results.Forbid();
                    }

                    newItem.Id = IdManager.Generate();

                    // Add to database
                    await newItem.CreateAsync(db);

                    return Results.Created(newItem.Id.ToString(), newItem);
                }
        }

        await ctx.Response.WriteAsync("Unsupported request type.");
        return Results.BadRequest();

    }
}
