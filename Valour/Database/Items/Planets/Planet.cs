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

    #endregion
}
