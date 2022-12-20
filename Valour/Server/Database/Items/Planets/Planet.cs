using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore.Storage;
using Valour.Api.Items.Planets;
using Valour.Server.Database.Items.Channels;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Server.Hubs;
using Valour.Server.Services;
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
    /// If the server should show up on the discovery tab
    /// </summary>
    [Column("discoverable")]
    public bool Discoverable { get; set; }

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
    public static Regex nameRegex = new Regex(@"^[\.a-zA-Z0-9 _-]+$");

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
        if (description is not null && description.Length > 128)
        {
            return new TaskResult(false, "Description must be under 128 characters.");
        }

        return TaskResult.SuccessResult;
    }
    
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
        
    }

    /// <summary>
    /// Adds a member to the server
    /// </summary>
    public async Task<TaskResult<PlanetMember>> AddMemberAsync(User user, ValourDB db, CoreHubService hubService, bool doTransaction = true)
    {
        if (await db.PlanetBans.AnyAsync(x => x.TargetId == user.Id && x.PlanetId == Id &&
            (x.TimeExpires != null && x.TimeExpires > DateTime.UtcNow)))
        {
            return new TaskResult<PlanetMember>(false, "You are banned from this planet.");
        }

        var oldMember = await db.PlanetMembers
            .IgnoreQueryFilters() // Allow soft-deleted members
            .FirstOrDefaultAsync(x => x.UserId == user.Id && x.PlanetId == Id);

        PlanetMember member;
        bool rejoin = false;

        // Already a member
        if (oldMember is not null)
        {
            if (!oldMember.IsDeleted)
            {
                return new TaskResult<PlanetMember>(false, "Already a member.", null);
            }
            else
            {
                member = oldMember;
                rejoin = true;
            }
        }
        else 
        {
            member = new PlanetMember()
            {
                Id = IdManager.Generate(),
                Nickname = user.Name,
                PlanetId = Id,
                UserId = user.Id
            };
        }

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
            if (rejoin)
            {
                member.IsDeleted = false;
                db.PlanetMembers.Update(member);
            }
            else
            {
                await db.PlanetMembers.AddAsync(member);
            }
            
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

        hubService.NotifyPlanetItemChange(member);

        Console.WriteLine($"User {user.Name} ({user.Id}) has joined {Name} ({Id})");

        return new TaskResult<PlanetMember>(true, "Success", member);
    }

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetRouteAsync(
        long id,
        ValourDB db)
    {
        var planet = await FindAsync<Planet>(id, db);
        return Results.Json(planet);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] Planet planet, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<Planet> logger)
    {
        var token = ctx.GetToken();
        if (planet is null)
            return ValourResult.BadRequest("Include planet in body.");

        var nameValid = ValidateName(planet.Name);
        if (!nameValid.Success)
            return ValourResult.BadRequest(nameValid.Message);

        if (planet.Description is null)
            planet.Description = String.Empty;

        var descValid = ValidateDescription(planet.Description);
        if (!descValid.Success)
            return ValourResult.BadRequest(descValid.Message);

        var user = await FindAsync<User>(token.UserId, db);

        if (!user.ValourStaff)
        {
            var ownedPlanets = await db.Planets.CountAsync(x => x.OwnerId == user.Id);
            if (ownedPlanets > MAX_OWNED_PLANETS)
                return ValourResult.BadRequest("You have reached the maximum owned planets!");
        }

        // Default image to start
        planet.IconUrl = "_content/Valour.Client/media/logo/logo-512.png";

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

            await planet.AddMemberAsync(user, db, hubService, false);
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return ValourResult.Problem("Sorry! We had an issue creating your planet. Try again?");
        }

        await tran.CommitAsync();

        return Results.Created(planet.GetUri(), planet);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] Planet planet,
        long id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<Planet> logger)
    {
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
        
        hubService.NotifyPlanetChange(planet);
        
        return Results.Json(planet);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.FullControl)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> DeleteRouteAsync(
        long id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<Planet> logger)
    {
        var authMember = ctx.GetMember();
        var planet = await FindAsync<Planet>(id, db);

        if (authMember.UserId != planet.OwnerId)
            return ValourResult.Forbid("You are not the owner of this planet.");

        // This is quite complicated.

        using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            var channels = await db.PlanetChatChannels.Where(x => x.PlanetId == id).ToListAsync();
            var categories = await db.PlanetCategoryChannels.Where(x => x.PlanetId == id).ToListAsync();
            var roles = await db.PlanetRoles.Where(x => x.PlanetId == id).ToListAsync();
            var members = await db.PlanetMembers.Where(x => x.PlanetId == id).ToListAsync();
            var invites = await db.PlanetInvites.Where(x => x.PlanetId == id).ToListAsync();

            // Channels (also deletes messages and nodes)
            foreach (var channel in channels)
            {
                await channel.DeleteAsync(db);
            }

            await db.SaveChangesAsync();

            // Categories (also deletes nodes)
            foreach (var category in categories)
            {
                await category.DeleteAsync(db);
            }

            await db.SaveChangesAsync();

            // Roles (Also deletes role membership)
            foreach (var role in roles)
            {
                await role.DeleteAsync(db);
            }

            await db.SaveChangesAsync();

            // Members
            foreach (var member in members)
            {
                await member.DeleteAsync(db);
            }

            await db.SaveChangesAsync();

            // Invites
            foreach (var invite in invites)
            {
                await invite.DeleteAsync(db);
            }

            await db.SaveChangesAsync();

            db.Remove(planet);

            await db.SaveChangesAsync();

            await tran.CommitAsync();
        }
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }
        
        hubService.NotifyPlanetDelete(planet);
        
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/channels"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChannelsRouteAsync(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
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
            else if (channel is PlanetVoiceChannel)
            {
                if (await channel.HasPermissionAsync(member, VoiceChannelPermissions.View, db))
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

    [ValourRoute(HttpVerbs.Get, "/{id}/chatchannels"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChatChannelsRouteAsync(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
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

    [ValourRoute(HttpVerbs.Get, "/{id}/voicechannels"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetVoiceChannelsRouteAsync(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
        var member = ctx.GetMember();
        var voiceChannels = await db.PlanetVoiceChannels.Where(x => x.PlanetId == id).ToListAsync();

        var allowedChannels = new List<PlanetVoiceChannel>();

        foreach (var channel in voiceChannels)
        {
            if (await channel.HasPermissionAsync(member, VoiceChannelPermissions.View, db))
            {
                allowedChannels.Add(channel);
            }
        }

        return Results.Json(allowedChannels);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/categories"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetCategoriesRouteAsync(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
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

    [ValourRoute(HttpVerbs.Get, "/{id}/channelids"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChannelIdsRouteAsync(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
        var channels = await db.PlanetChannels.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(channels);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/chatchannelids"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetChatChannelIdsRouteAsync(
        long id,
        ValourDB db)
    {
        var chatChannels = await db.PlanetChatChannels.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(chatChannels);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/voicechannelids"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetVoiceChannelIdsRouteAsync(
        long id,
        ValourDB db)
    {
        var voiceChannels = await db.PlanetVoiceChannels.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(voiceChannels);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/categoryids"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetCategoryIdsRouteAsync(
        long id,
        ValourDB db)
    {
        var categories = await db.PlanetCategoryChannels.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(categories);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/memberinfo"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetMemberInfoRouteAsync(
        long id, 
        ValourDB db, 
        int page = 0)
    {
        var members = db.PlanetMembers
            .Where(x => x.PlanetId == id)
            .OrderBy(x => x.Id);

        var totalCount = await members.CountAsync();

        var roleInfo = await members.Select(x => new
        {
            member = x,
            user = x.User,
            roleIds = x.RoleMembership.Select(x => x.RoleId)
        })
            .Where(x => !x.user.Disabled)
            .Skip(page * 100)
            .Take(100)
            .ToListAsync();

        return Results.Json(new { members = roleInfo, totalCount = totalCount });
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/roles"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetRolesRouteAsync(
        long id, 
        ValourDB db)
    {
        var roles = await db.PlanetRoles.Where(x => x.PlanetId == id).ToListAsync();
        return Results.Json(roles);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/roleids"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id")]
    public static async Task<IResult> GetRoleIdsRouteAsync(
        long id, 
        ValourDB db)
    {
        var roles = await db.PlanetRoles.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(roles);
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/roleorder"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired("id", PlanetPermissionsEnum.ManageRoles)]
    public static async Task<IResult> SetRoleOrderRouteAsync(
        [FromBody] long[] order, 
        long id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<Planet> logger)
    {
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
            hubService.NotifyPlanetItemChange(role);
        }

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/invites"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id", PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> GetInvitesRouteAsync(
        long id, 
        ValourDB db)
    {
        var invites = await db.PlanetInvites.Where(x => x.PlanetId == id).ToListAsync();
        return Results.Json(invites);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/inviteids"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id", PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> GetInviteIdsRouteAsync(
        long id, 
        ValourDB db)
    {
        var invites = await db.PlanetInvites.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(invites);
    }

    [ValourRoute(HttpVerbs.Get, "/discoverable"), TokenRequired]
    public static async Task<IResult> GetDiscoverables(ValourDB db)
    {
        var planets = await db.Planets.Include(x => x.Members)
                                      .Where(x => x.Public && x.Discoverable)
                                      .OrderByDescending(x => x.Members.Count())
                                      .ToListAsync();

        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/discover"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Invites)]
    public static async Task<IResult> JoinDiscoverable(
        long id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService)
    {
        long userId = ctx.GetToken().UserId;

        if (await db.PlanetBans.AnyAsync(x => x.TargetId == userId && x.PlanetId == id))
            return Results.BadRequest("User is banned from the planet");

        if (await db.PlanetMembers.AnyAsync(x => x.UserId == userId && x.PlanetId == id))
            return Results.BadRequest("User is already a member");

        var planet = await FindAsync(id, db);

        if (!planet.Public)
            return Results.BadRequest("Planet is set to private");

        if (!planet.Discoverable)
            return Results.BadRequest("Planet is not discoverable");

        TaskResult<PlanetMember> result = await planet.AddMemberAsync(await FindAsync<User>(userId, db), db, hubService);

        if (result.Success)
            return Results.Created(result.Data.GetUri(), result.Data);
        else
            return ValourResult.Problem(result.Message);
    }

    #endregion
}
