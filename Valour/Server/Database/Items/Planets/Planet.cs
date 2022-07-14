using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database.Items.Planets.Channels;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets;

namespace Valour.Server.Database.Items.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

/// <summary>
/// This class exists to add server funtionality to the Planet class.
/// </summary>
[Table("planets")]
public class Planet : Item, ISharedPlanet
{
    // Constant planet variables //

    /// <summary>
    /// The maximum planets a user is allowed to have. This will increase after 
    /// the alpha period is complete.
    /// </summary>
    [JsonIgnore]
    public const int MAX_OWNED_PLANETS = 5;

    /// <summary>
    /// The maximum planets a user is allowed to join. This will increase after the 
    /// alpha period is complete.
    /// </summary>
    [JsonIgnore]
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
    public virtual ICollection<PlanetInvite> Invites { get; set; }

    [ForeignKey("DefaultRoleId")]
    [JsonIgnore]
    public virtual PlanetRole DefaultRole { get; set; }

    [ForeignKey("PrimaryChannelId")]
    [JsonIgnore]
    public virtual PlanetChatChannel PrimaryChannel { get; set; }

    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    [Column("owner_id")]
    public long OwnerId { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// The image url for the planet 
    /// </summary>
    [Column("icon_url")]
    public string IconUrl { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    [Column("description")]
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    [Column("public")]
    public bool Public { get; set; }

    /// <summary>
    /// The default role for the planet
    /// </summary>
    [Column("default_role_id")]
    public long? DefaultRoleId { get; set; }

    /// <summary>
    /// The id of the main channel of the planet
    /// </summary>
    [Column("primary_channel_id")]
    public long? PrimaryChannelId { get; set; }

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
    public async Task<bool> IsMemberAsync(long userId, ValourDB db) =>
        await db.PlanetMembers.AnyAsync(x => x.PlanetId == this.Id && x.UserId == userId);

    /// <summary>
    /// Returns if a given user is a member (async)
    /// </summary>
    public async Task<bool> IsMemberAsync(User user, ValourDB db) =>
        await IsMemberAsync(user.Id, db);

    /// <summary>
    /// Returns the primary channel for the planet
    /// </summary>
    public async Task<PlanetChatChannel> GetPrimaryChannelAsync(ValourDB db) =>
        PrimaryChannel ??= await FindAsync<PlanetChatChannel>(PrimaryChannelId, db);

    /// <summary>
    /// Returns the default role for the planet
    /// </summary>
    public async Task<PlanetRole> GetDefaultRole(ValourDB db) =>
        DefaultRole ??= await FindAsync<PlanetRole>(DefaultRoleId, db);

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
        if (member.UserId == OwnerId)
            return true;

        // Get user main role
        var mainRole = await member.GetPrimaryRoleAsync(db);

        // Return permission state
        return mainRole.HasPermission(permission);
    }

    /// <summary>
    /// Adds a member to the server
    /// </summary>
    public async Task<TaskResult<PlanetMember>> AddMemberAsync(User user, ValourDB db, bool doTransaction = true)
    {
        // Already a member
        if (await db.PlanetMembers.AnyAsync(x => x.UserId == user.Id && x.PlanetId == Id))
        {
            return new TaskResult<PlanetMember>(false, "Already a member.", null);
        }

        PlanetMember member = new PlanetMember()
        {
            Id = IdManager.Generate(),
            Nickname = user.Name,
            PlanetId = Id,
            UserId = user.Id
        };

        // Add to default planet role
        PlanetRoleMember rolemember = new PlanetRoleMember()
        {
            Id = IdManager.Generate(),
            PlanetId = Id,
            UserId = user.Id,
            RoleId = DefaultRoleId.Value,
            MemberId = member.Id
        };


        IDbContextTransaction trans = null;

        if (doTransaction)
            trans = await db.Database.BeginTransactionAsync();

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

        if (doTransaction)
            await trans.CommitAsync();

        Console.WriteLine($"User {user.Name} ({user.Id}) has joined {Name} ({Id})");

        return new TaskResult<PlanetMember>(true, "Success", member);
    }

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetRouteAsync(HttpContext ctx, ulong id)
    {
        var db = ctx.GetDb();
        var planet = await FindAsync<Planet>(id, db);

        return Results.Json(planet);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
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

        var user = await FindAsync<User>(token.UserId, db);

        if (!user.ValourStaff)
        {
            var ownedPlanets = await db.Planets.CountAsync(x => x.OwnerId == user.Id);
            if (ownedPlanets > MAX_OWNED_PLANETS)
                return ValourResult.Forbid("You have reached the maximum owned planets!");
        }

        // Default image to start
        planet.IconUrl = "/media/logo/logo-512.png";

        planet.Id = IdManager.Generate();
        planet.OwnerId = user.Id;
        planet.PrimaryChannelId = null;
        planet.DefaultRoleId = null;

        // Create general category
        var category = new PlanetCategoryChannel()
        {
            Id = IdManager.Generate(),
            Name = "General",
            ParentId = null,
            PlanetId = planet.Id,
            Description = "General category",
            Position = 0
        };

        // Create general chat channel
        var channel = new PlanetChatChannel()
        {
            Id = IdManager.Generate(),
            PlanetId = planet.Id,
            Name = "General",
            MessageCount = 0,
            Description = "General chat channel",
            ParentId = category.Id
        };

        // Create default role
        var defaultRole = new PlanetRole()
        {
            Id = IdManager.Generate(),
            PlanetId = planet.Id,
            Position = int.MaxValue,
            Blue = 255,
            Green = 255,
            Red = 255,
            Name = "@everyone"
        };

        using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            await db.Planets.AddAsync(planet);

            await db.SaveChangesAsync();

            await db.PlanetCategoryChannels.AddAsync(category);
            await db.PlanetChatChannels.AddAsync(channel);
            await db.PlanetRoles.AddAsync(defaultRole);

            await db.SaveChangesAsync();

            planet.PrimaryChannelId = channel.Id;
            planet.DefaultRoleId = defaultRole.Id;

            await db.SaveChangesAsync();

            await planet.AddMemberAsync(user, db, false);
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

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, long id, [FromBody] Planet planet,
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
        if (old.OwnerId != planet.OwnerId)
        {
            // Only owner can do this
            if (member.UserId != old.OwnerId)
                return ValourResult.Forbid("Only a planet owner can transfer ownership.");

            // Ensure new owner is a member of the planet
            if (!await old.IsMemberAsync(planet.OwnerId, db))
                return Results.BadRequest("You cannot transfer ownership to a non-member.");

            var ownedPlanets = await db.Planets.CountAsync(x => x.OwnerId == planet.OwnerId);
            if (ownedPlanets >= MAX_OWNED_PLANETS)
                return Results.BadRequest("That user has the maximum owned planets!");
        }

        if (old.DefaultRoleId != planet.DefaultRoleId)
            return Results.BadRequest("You cannot change the default role. Change the permissions on it instead.");

        if (old.PrimaryChannelId != planet.PrimaryChannelId)
        {
            // Ensure new channel exists and belongs to the planet
            var newChannel = await db.PlanetChatChannels.FirstOrDefaultAsync(x => x.PlanetId == id && x.Id == planet.PrimaryChannelId);

            if (newChannel is null)
                return ValourResult.NotFound<PlanetChatChannel>();
        }

        if (old.IconUrl != planet.IconUrl)
            return Results.BadRequest("Use the upload API to change the planet icon.");

        try
        {
            db.Entry(old).State = EntityState.Detached;
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

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.FullControl)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> DeleteRouteAsync(HttpContext ctx, long id,
        ILogger<Planet> logger)
    {
        var db = ctx.GetDb();
        var authMember = ctx.GetMember();
        var planet = await FindAsync<Planet>(id, db);

        if (authMember.UserId != planet.OwnerId)
            return ValourResult.Forbid("You are not the owner of this planet.");

        // This is quite complicated.

        using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            var channels = db.PlanetChatChannels.Where(x => x.PlanetId == id);
            var categories = db.PlanetCategoryChannels.Where(x => x.PlanetId == id);
            var roles = db.PlanetRoles.Where(x => x.PlanetId == id);
            var members = db.PlanetMembers.Where(x => x.PlanetId == id);
            var invites = db.PlanetInvites.Where(x => x.PlanetId == id);

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
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/channels"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChannelsRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        var channels = await db.PlanetChannels.Where(x => x.PlanetId == id).ToListAsync();
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

    [ValourRoute(HttpVerbs.Get, "/{id}/chatchannels"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChatChannelsRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();
        var chatChannels = await db.PlanetChatChannels.Where(x => x.PlanetId == id).ToListAsync();

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

    [ValourRoute(HttpVerbs.Get, "/{id}/categories"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetCategoriesRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();
        var categories = await db.PlanetCategoryChannels.Where(x => x.PlanetId == id).ToListAsync();
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

    [ValourRoute(HttpVerbs.Get, "/{id}/channelids"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChannelIdsRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var channels = await db.PlanetChannels.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(channels);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/chatchannelids"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChatChannelIdsRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var chatChannels = await db.PlanetChatChannels.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(chatChannels);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/categoryids"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetCategoryIdsRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var categories = await db.PlanetCategoryChannels.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(categories);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/memberinfo"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetMemberInfoRouteAsync(HttpContext ctx, long id, int page = 0)
    {
        var db = ctx.GetDb();

        var members = db.PlanetMembers
            .Where(x => x.PlanetId == id);

        var totalCount = await members.CountAsync();

        var roleInfo = await members.Select(x => new
        {
            member = x,
            user = x.User,
            roleIds = x.RoleMembership.Select(x => x.RoleId)
        })
            .Skip(page * 100)
            .Take(100)
            .ToListAsync();

        return Results.Json(new { members = roleInfo, totalCount = totalCount });
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/roles"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetRolesRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var roles = await db.PlanetRoles.Where(x => x.PlanetId == id).ToListAsync();

        return Results.Json(roles);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/roleids"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetRoleIdsRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var roles = await db.PlanetRoles.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();

        return Results.Json(roles);
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/roleorder"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired("id", PlanetPermissionsEnum.ManageRoles)]
    public static async Task<IResult> SetRoleOrderRouteAsync(HttpContext ctx, long id, [FromBody] long[] order,
        ILogger<Planet> logger)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        var authority = await member.GetAuthorityAsync(db);

        // Remove duplicates
        order = order.Distinct().ToArray();

        // Ensure every role is accounted for
        var totalRoles = await db.PlanetRoles.CountAsync(x => x.PlanetId == id);

        if (totalRoles != order.Length)
            return Results.BadRequest("Your order does not contain all the planet roles.");

        using var tran = await db.Database.BeginTransactionAsync();

        List<PlanetRole> roles = new();

        try
        {
            int pos = 0;

            foreach (var roleId in order)
            {
                var role = await FindAsync<PlanetRole>(roleId, db);

                if (role is null)
                    return ValourResult.NotFound<PlanetRole>();

                if (role.PlanetId != id)
                    return Results.BadRequest($"Role {role.Id} does not belong to planet {id}");

                role.Position = pos;

                db.PlanetRoles.Update(role);

                roles.Add(role);

                pos++;
            }

            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        foreach (var role in roles)
        {
            PlanetHub.NotifyPlanetItemChange(role);
        }

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/invites"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id", PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> GetInvitesRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var invites = await db.PlanetInvites.Where(x => x.PlanetId == id).ToListAsync();

        return Results.Json(invites);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/inviteids"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id", PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> GetInviteIdsRouteAsync(HttpContext ctx, long id)
    {
        var db = ctx.GetDb();
        var invites = await db.PlanetInvites.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();

        return Results.Json(invites);
    }


    #endregion
}
