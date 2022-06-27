using Microsoft.EntityFrameworkCore;
using Valour.Shared;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using Valour.Database.Items.Users;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Items;
using Valour.Database.Items.Planets.Channels;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets;
using Microsoft.AspNetCore.Http;
using Valour.Database.Attributes;
using System.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using Valour.Database.Extensions;
using Valour.Shared.Http;
using Microsoft.Extensions.Logging;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets;


/// <summary>
/// This class exists to add server funtionality to the Planet class.
/// </summary>
public class Planet : Item, ISharedPlanet
{
    // Constant planet variables //

    /// <summary>
    /// The maximum planets a user is allowed to have. This will increase after 
    /// the alpha period is complete.
    /// </summary>
    public const int MAX_OWNED_PLANETS = 5;

    /// <summary>
    /// The maximum planets a user is allowed to join. This will increase after the 
    /// alpha period is complete.
    /// </summary>
    public const int MAX_JOINED_PLANETS = 20;

    [InverseProperty("Planet")]
    [JsonIgnore]
    public virtual ICollection<PlanetRole> Roles { get; set; }

    [InverseProperty("Planet")]
    [JsonIgnore]
    public virtual ICollection<PlanetMember> Members { get; set; }

    [InverseProperty("Planet")]
    [JsonIgnore]
    public virtual ICollection<PlanetChatChannel> ChatChannels { get; set; }

    [InverseProperty("Planet")]
    [JsonIgnore]
    public virtual ICollection<PlanetCategoryChannel> Categories { get; set; }

    [InverseProperty("Planet")]
    [JsonIgnore]
    public virtual ICollection<Invite> Invites { get; set; }

    [InverseProperty("Default_Role_Id")]
    [JsonIgnore]
    public virtual PlanetRole DefaultRole { get; set; }

    [InverseProperty("Primary_Channel_Id")]
    [JsonIgnore]
    public virtual PlanetChatChannel PrimaryChannel { get; set; }

    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    public ulong Owner_Id { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The image url for the planet 
    /// </summary>
    public string IconUrl { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    public bool Public { get; set; }

    /// <summary>
    /// The default role for the planet
    /// </summary>
    public ulong Default_Role_Id { get; set; }

    /// <summary>
    /// The id of the main channel of the planet
    /// </summary>
    public ulong Primary_Channel_Id { get; set; }

    [NotMapped]
    public override ItemType ItemType => ItemType.Planet;

    [JsonIgnore]
    public static Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

    /// <summary>
    /// Validates that a given name is allowable for a planet
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new TaskResult(false, "Planet names cannot be empty.");
        }

        if (name.Length > 32)
        {
            return new TaskResult(false, "Planet names must be 32 characters or less.");
        }

        if (!nameRegex.IsMatch(name))
        {
            return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
        }

        return new TaskResult(true, "The given name is valid.");
    }

    /// <summary>
    /// Validates that a given description is alloweable for a planet
    /// </summary>
    public static TaskResult ValidateDescription(string description)
    {
        if (description.Length > 128)
        {
            return new TaskResult(false, "Description must be under 128 characters.");
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Retrieves a planet for the given id
    /// </summary>
    public static async Task<Planet> FindAsync(ulong id, ValourDB db) =>
        await FindAsync<Planet>(id, db);

    /// <summary>
    /// Returns if a given user id is a member (async)
    /// </summary>
    public async Task<bool> IsMemberAsync(ulong user_id, ValourDB db) =>
        await db.PlanetMembers.AnyAsync(x => x.Planet_Id == this.Id && x.User_Id == user_id);

    /// <summary>
    /// Returns if a given user is a member (async)
    /// </summary>
    public async Task<bool> IsMemberAsync(User user, ValourDB db) =>
        await IsMemberAsync(user.Id, db);

    /// <summary>
    /// Returns the primary channel for the planet
    /// </summary>
    public async Task<PlanetChatChannel> GetPrimaryChannelAsync(ValourDB db) =>
        PrimaryChannel ??= await FindAsync<PlanetChatChannel>(Primary_Channel_Id, db);

    /// <summary>
    /// Returns the default role for the planet
    /// </summary>
    public async Task<PlanetRole> GetDefaultRole(ValourDB db) =>
        DefaultRole ??= await FindAsync<PlanetRole>(Default_Role_Id, db);

    /// <summary>
    /// Returns all roles within the planet
    /// </summary>
    public async Task<ICollection<PlanetRole>> GetRolesAsync(ValourDB db) =>
        Roles ??= await db.Attach(this).Collection(x => x.Roles).Query().ToListAsync();


    /// <summary>
    /// Returns if the given user has the given planet permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetMember member, PlanetPermission permission, ValourDB db)
    {
        // Special case for viewing planets
        if (permission.Value == PlanetPermissions.View.Value)
        {
            if (Public || (member != null))
            {
                return true;
            }
        }

        // At this point all permissions require membership
        if (member is null)
            return false;

        // Owner has all permissions
        if (member.User_Id == Owner_Id)
            return true;

        // Get user main role
        var mainRole = await member.GetPrimaryRoleAsync(db);

        // Return permission state
        return mainRole.HasPermission(permission);
    }

    /// <summary>
    /// Adds a member to the server
    /// </summary>
    public async Task<TaskResult<PlanetMember>> AddMemberAsync(User user, ValourDB db)
    {
        // Already a member
        if (await db.PlanetMembers.AnyAsync(x => x.User_Id == user.Id && x.Planet_Id == Id))
        {
            return new TaskResult<PlanetMember>(false, "Already a member.", null);
        }

        PlanetMember member = new PlanetMember()
        {
            Id = IdManager.Generate(),
            Nickname = user.Name,
            Planet_Id = Id,
            User_Id = user.Id
        };

        // Add to default planet role
        PlanetRoleMember rolemember = new PlanetRoleMember()
        {
            Id = IdManager.Generate(),
            Planet_Id = Id,
            User_Id = user.Id,
            Role_Id = Default_Role_Id,
            Member_Id = member.Id
        };

        var trans = await db.Database.BeginTransactionAsync();

        try
        {
            await db.PlanetMembers.AddAsync(member);
            await db.PlanetRoleMembers.AddAsync(rolemember);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            await trans.RollbackAsync();
            return new TaskResult<PlanetMember>(false, e.Message);
        }

        await trans.CommitAsync();

        Console.WriteLine($"User {user.Name} ({user.Id}) has joined {Name} ({Id})");

        return new TaskResult<PlanetMember>(true, "Success", member);
    }

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var planet = await FindAsync<Planet>(id, db);

        return Results.Json(planet);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDB]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, [FromBody] Planet planet,
        ILogger<Planet> logger)
    {
        var token = ctx.GetToken();
        var db = ctx.GetDb();

        if (planet is null)
            return Results.BadRequest("Include planet in body.");

        var nameValid = ValidateName(planet.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(planet.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        var user = await FindAsync<User>(token.User_Id, db);

        if (!user.ValourStaff)
        {
            var ownedPlanets = await db.Planets.CountAsync(x => x.Owner_Id == user.Id);
            if (ownedPlanets > MAX_OWNED_PLANETS)
                return ValourResult.Forbid("You have reached the maximum owned planets!");
        }

        // Default image to start
        planet.IconUrl = "/media/logo/logo-512.png";

        planet.Id = IdManager.Generate();
        planet.Owner_Id = user.Id;

        // Create general category
        var category = new PlanetCategoryChannel()
        {
            Id = IdManager.Generate(),
            Name = "General",
            Parent_Id = null,
            Planet_Id = planet.Id,
            Description = "General category",
            Position = 0
        };

        // Create general chat channel
        var channel = new PlanetChatChannel()
        {
            Id = IdManager.Generate(),
            Planet_Id = planet.Id,
            Name = "General",
            MessageCount = 0,
            Description = "General chat channel",
            Parent_Id = category.Id
        };

        // Create default role
        var defaultRole = new PlanetRole()
        {
            Id = IdManager.Generate(),
            Planet_Id = planet.Id,
            Position = uint.MaxValue,
            Color_Blue = 255,
            Color_Green = 255,
            Color_Red = 255,
            Name = "@everyone"
        };

        var tran = await db.Database.BeginTransactionAsync();

        try
        {
            await db.Planets.AddAsync(planet);
            await db.PlanetCategoryChannels.AddAsync(category);
            await db.PlanetChatChannels.AddAsync(channel);

            planet.Primary_Channel_Id = channel.Id;

            await db.PlanetRoles.AddAsync(defaultRole);

            planet.Default_Role_Id = defaultRole.Id;

            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        return Results.Created(planet.GetUri(), planet);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, ulong id, [FromBody] Planet planet,
        ILogger<Planet> logger)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        var old = await FindAsync<Planet>(id, db);

        if (!await member.HasPermissionAsync(PlanetPermissions.Manage, db))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        if (planet is null)
            return Results.BadRequest("Include planet in body.");

        if (planet.Id != id)
            return Results.BadRequest("Id cannot be changed.");

        var nameValid = ValidateName(planet.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(planet.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        // Owner change check
        if (old.Owner_Id != planet.Owner_Id)
        {
            // Only owner can do this
            if (member.User_Id != old.Owner_Id)
                return ValourResult.Forbid("Only a planet owner can transfer ownership.");

            // Ensure new owner is a member of the planet
            if (!await old.IsMemberAsync(planet.Owner_Id, db))
                return Results.BadRequest("You cannot transfer ownership to a non-member.");

            var ownedPlanets = await db.Planets.CountAsync(x => x.Owner_Id == planet.Owner_Id);
            if (ownedPlanets >= MAX_OWNED_PLANETS)
                return Results.BadRequest("That user has the maximum owned planets!");
        }

        if (old.Default_Role_Id != planet.Default_Role_Id)
            return Results.BadRequest("You cannot change the default role. Change the permissions on it instead.");

        if (old.Primary_Channel_Id != planet.Primary_Channel_Id)
        {
            // Ensure new channel exists and belongs to the planet
            var newChannel = await db.PlanetChatChannels.FirstOrDefaultAsync(x => x.Planet_Id == id && x.Id == planet.Primary_Channel_Id);

            if (newChannel is null)
                return ValourResult.NotFound<PlanetChatChannel>();
        }

        if (old.IconUrl != planet.IconUrl)
            return Results.BadRequest("Use the upload API to change the planet icon.");

        try
        {
            db.Planets.Update(planet);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetChange(planet);

        return Results.Json(planet);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> DeleteRouteAsync(HttpContext ctx, ulong id, 
        ILogger<Planet> logger)
    {
        var db = ctx.GetDb();
        var authMember = ctx.GetMember();
        var planet = await FindAsync<Planet>(id, db);

        if (authMember.User_Id != planet.Owner_Id)
            return ValourResult.Forbid("You are not the owner of this planet.");

        // This is quite complicated.

        var tran = await db.Database.BeginTransactionAsync();

        try
        {
            var channels = db.PlanetChatChannels.Where(x => x.Planet_Id == id);
            var categories = db.PlanetCategoryChannels.Where(x => x.Planet_Id == id);
            var roles = db.PlanetRoles.Where(x => x.Planet_Id == id);
            var members = db.PlanetMembers.Where(x => x.Planet_Id == id);
            var invites = db.PlanetInvites.Where(x => x.Planet_Id == id);

            // Channels (also deletes messages and nodes)
            foreach (var channel in channels)
            {
                await channel.DeleteAsync(db);
            }

            // Categories (also deletes nodes)
            foreach (var category in categories)
            {
                await category.DeleteAsync(db);
            }

            // Roles (Also deletes role membership)
            foreach (var role in roles)
            {
                await role.DeleteAsync(db);
            }

            // Members
            foreach (var member in members)
            {
                await member.DeleteAsync(db);
            }

            // Invites
            foreach (var invite in invites)
            {
                await invite.DeleteAsync(db);
            }

            db.Remove(planet);

            await db.SaveChangesAsync();
        }
        catch(System.Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "{id}/channels"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChannelsRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        var channels = await db.PlanetChannels.Where(x => x.Planet_Id == id).ToListAsync();
        var allowedChannels = new List<PlanetChannel>();

        foreach (var channel in channels)
        {
            if (channel is PlanetChatChannel)
            {
                if (await channel.HasPermissionAsync(member, ChatChannelPermissions.View, db))
                {
                    allowedChannels.Add(channel);
                }
            }
            else if (channel is PlanetCategoryChannel)
            {
                if (await channel.HasPermissionAsync(member, CategoryPermissions.View, db))
                {
                    allowedChannels.Add(channel);
                }
            }
        }

        return Results.Json(allowedChannels);
    }

    [ValourRoute(HttpVerbs.Get, "{id}/chatchannels"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChatChannelsRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();
        var chatChannels = await db.PlanetChatChannels.Where(x => x.Planet_Id == id).ToListAsync();

        var allowedChannels = new List<PlanetChatChannel>();

        foreach (var channel in chatChannels)
        {
            if (await channel.HasPermissionAsync(member, ChatChannelPermissions.View, db))
            {
                allowedChannels.Add(channel);
            }
        }

        return Results.Json(allowedChannels);
    }

    [ValourRoute(HttpVerbs.Get, "{id}/categories"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetCategoriesRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();
        var categories = await db.PlanetCategoryChannels.Where(x => x.Planet_Id == id).ToListAsync();
        var allowedCategories = new List<PlanetCategoryChannel>();

        foreach (var category in categories)
        {
            if (await category.HasPermissionAsync(member, CategoryPermissions.View, db))
            {
                allowedCategories.Add(category);
            }
        }

        return Results.Json(allowedCategories);
    }

    [ValourRoute(HttpVerbs.Get, "{id}/channelids"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChannelIdsRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var channels = await db.PlanetChannels.Where(x => x.Planet_Id == id).Select(x => x.Id).ToListAsync();
        return Results.Json(channels);
    }

    [ValourRoute(HttpVerbs.Get, "{id}/chatchannelids"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChatChannelIdsRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var chatChannels = await db.PlanetChatChannels.Where(x => x.Planet_Id == id).Select(x => x.Id).ToListAsync();
        return Results.Json(chatChannels);
    }

    [ValourRoute(HttpVerbs.Get, "{id}/categoryids"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetCategoryIdsRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var categories = await db.PlanetCategoryChannels.Where(x => x.Planet_Id == id).Select(x => x.Id).ToListAsync();
        return Results.Json(categories);
    }

    [ValourRoute(HttpVerbs.Get, "{id}/memberinfo"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("id")]
    private static async Task<IResult> GetMemberInfo(HttpContext ctx, ulong id, int page = 0)
    {
        var db = ctx.GetDb();

        var members = db.PlanetMembers
            .Where(x => x.Planet_Id == id);

        var totalCount = await members.CountAsync();

        var roleInfo = await members.Select(x => new
            {
                member = x,
                user = x.User,
                roleIds = x.RoleMembership.Select(x => x.Role_Id)
            })
            .Skip(page * 100)
            .Take(100)
            .ToListAsync();

        return Results.Json((members: roleInfo, totalCount: totalCount));
    }

    #endregion
}
