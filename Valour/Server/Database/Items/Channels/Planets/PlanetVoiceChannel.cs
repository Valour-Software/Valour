using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;

namespace Valour.Server.Database.Items.Channels.Planets;

[Table("planet_voice_channels")]
public class PlanetVoiceChannel : PlanetChannel, IPlanetItem, ISharedPlanetVoiceChannel
{
    #region IPlanetItem Implementation

    [JsonIgnore]
    public override string BaseRoute =>
        $"api/planet/{{planetId}}/{nameof(PlanetVoiceChannel)}";

    #endregion


    [NotMapped]
    public override PermissionsTargetType PermissionsTargetType
        => PermissionsTargetType.PlanetVoiceChannel;

    /// <summary>
    /// The regex used for name validation
    /// </summary>
    [JsonIgnore]
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public override async Task<bool> HasPermissionAsync(PlanetMember member, Permission permission, ValourDB db)
    {
        await GetPlanetAsync(db);

        if (Planet.OwnerId == member.UserId)
            return true;

        // If true, we just ask the category
        if (InheritsPerms)
        {
            return await (await GetParentAsync(db)).HasPermissionAsync(member, permission, db);
        }


        // Load permission data
        await db.Entry(member).Collection(x => x.RoleMembership)
                              .Query()
                              .Where(x => x.PlanetId == Planet.Id)
                              .Include(x => x.Role)
                              .ThenInclude(x => x.PermissionNodes.Where(x => x.TargetId == Id))
                              .OrderBy(x => x.Role.Position)
                              .LoadAsync();

        // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
        // If we get an undecided, we continue to the next role down
        foreach (var roleMembership in member.RoleMembership)
        {
            var role = roleMembership.Role;
            // For some reason, we need to make sure we get the node that has the same targetId as this channel
            // When loading I suppose it grabs all the nodes even if the target is not the same?
            PermissionsNode node = role.PermissionNodes.FirstOrDefault(x => x.TargetId == Id && x.TargetType == PermissionsTargetType.PlanetVoiceChannel);

            // If we are dealing with the default role and the behavior is undefined, we fall back to the default permissions
            if (node == null)
            {
                if (role.Id == Planet.DefaultRoleId)
                {
                    return Permission.HasPermission(VoiceChannelPermissions.Default, permission);
                }

                continue;
            }

            PermissionState state = node.GetPermissionState(permission);

            if (state == PermissionState.Undefined)
            {
                continue;
            }
            else if (state == PermissionState.True)
            {
                return true;
            }
            else
            {
                return false;
            }

        }

        // No roles ever defined behavior: resort to false.
        return false;
    }

    public void Delete(ValourDB db)
    {
        // Remove permission nodes
        db.PermissionsNodes.RemoveRange(
            db.PermissionsNodes.Where(x => x.TargetId == Id)
        );

        // Remove channel
        db.PlanetVoiceChannels.Remove(this);
    }

    /// <summary>
    /// Returns all members who can see this channel
    /// </summary>
    public async Task<List<PlanetMember>> GetChannelMembersAsync(ValourDB db)
    {
        // TODO: It would be more efficient to check each role and then get all users in those roles
        // rather than going member by member. Revisit this later.

        List<PlanetMember> members = new List<PlanetMember>();

        var planetMembers = db.PlanetMembers.Include(x => x.RoleMembership).Where(x => x.PlanetId == PlanetId);

        foreach (var member in planetMembers)
        {
            if (await HasPermissionAsync(member, VoiceChannelPermissions.View, db))
            {
                members.Add(member);
            }
        }

        return members;
    }

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired, VoiceChannelPermsRequired(VoiceChannelPermissionsEnum.View)]
    public static IResult GetRoute(HttpContext ctx, long id) =>
        Results.Json(ctx.GetItem<PlanetVoiceChannel>(id));

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageChannels)]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, long planetId, [FromBody] PlanetVoiceChannel channel,
        ILogger<PlanetVoiceChannel> logger)
    {
        // Get resources
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        if (channel.PlanetId != planetId)
            return Results.BadRequest("PlanetId mismatch.");

        var nameValid = ValidateName(channel.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(channel.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        var positionValid = await ValidateParentAndPosition(db, channel);
        if (!positionValid.Success)
            return Results.BadRequest(positionValid.Message);

        // Ensure user has permission for parent category management
        if (channel.ParentId is not null)
        {
            var parent_cat = await db.PlanetCategoryChannels.FindAsync(channel.ParentId);
            if (!await parent_cat.HasPermissionAsync(member, CategoryPermissions.ManageCategory, db))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        channel.Id = IdManager.Generate();

        try
        {
            await db.PlanetVoiceChannels.AddAsync(channel);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(channel);

        return Results.Created(channel.GetUri(), channel);
    }

    [ValourRoute(HttpVerbs.Post, "/detailed"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageChannels)]
    public static async Task<IResult> PostRouteWithDetailsAsync(HttpContext ctx, long planetId,
        [FromBody] CreatePlanetVoiceChannelRequest request, ILogger<PlanetVoiceChannel> logger)
    {
        // Get resources
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        var channel = request.Channel;

        if (channel.PlanetId != planetId)
            return Results.BadRequest("PlanetId mismatch.");

        var nameValid = ValidateName(channel.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(channel.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        var positionValid = await ValidateParentAndPosition(db, channel);
        if (!positionValid.Success)
            return Results.BadRequest(positionValid.Message);

        // Ensure user has permission for parent category management
        if (channel.ParentId is not null)
        {
            var parent_cat = await db.PlanetCategoryChannels.FindAsync(channel.ParentId);
            if (!await parent_cat.HasPermissionAsync(member, CategoryPermissions.ManageCategory, db))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        channel.Id = IdManager.Generate();

        List<PermissionsNode> nodes = new();

        // Create nodes
        foreach (var nodeReq in request.Nodes)
        {
            var node = nodeReq;
            node.TargetId = channel.Id;
            node.PlanetId = planetId;

            var role = await FindAsync<PlanetRole>(node.RoleId, db);
            if (role.GetAuthority() > await member.GetAuthorityAsync(db))
                return ValourResult.Forbid("A permission node's role has higher authority than you.");

            node.Id = IdManager.Generate();

            nodes.Add(node);
        }

        var tran = await db.Database.BeginTransactionAsync();

        try
        {
            await db.PlanetVoiceChannels.AddAsync(channel);
            await db.SaveChangesAsync();

            await db.PermissionsNodes.AddRangeAsync(nodes);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        PlanetHub.NotifyPlanetItemChange(channel);

        return Results.Created(channel.GetUri(), channel);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageChannels)]
    [VoiceChannelPermsRequired(VoiceChannelPermissionsEnum.ManageChannel)]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, long id, [FromBody] PlanetVoiceChannel channel,
        ILogger<PlanetChatChannel> logger)
    {
        // Get resources
        var db = ctx.GetDb();
        var old = ctx.GetItem<PlanetVoiceChannel>(id);

        // Validation
        if (old.Id != channel.Id)
            return Results.BadRequest("Cannot change Id.");
        if (old.PlanetId != channel.PlanetId)
            return Results.BadRequest("Cannot change PlanetId.");

        var nameValid = ValidateName(channel.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(channel.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        var positionValid = await ValidateParentAndPosition(db, channel);
        if (!positionValid.Success)
            return Results.BadRequest(positionValid.Message);

        // Update
        try
        {
            db.Entry(old).State = EntityState.Detached;
            db.PlanetVoiceChannels.Update(channel);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(channel);

        // Response
        return Results.Ok(channel);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageChannels)]
    [VoiceChannelPermsRequired(VoiceChannelPermissionsEnum.ManageChannel)]
    public static async Task<IResult> DeleteRouteAsync(HttpContext ctx, long id, long planetId,
        ILogger<PlanetVoiceChannel> logger)
    {
        var db = ctx.GetDb();
        var channel = ctx.GetItem<PlanetVoiceChannel>(id);

        // Always use transaction for multi-step DB operations
        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            channel.Delete(db);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            await transaction.RollbackAsync();
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemDelete(channel);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/checkperm/{memberId}/{value}"), TokenRequired, InjectDb]
    [PlanetMembershipRequired]
    [VoiceChannelPermsRequired(VoiceChannelPermissionsEnum.View)]
    public static async Task<IResult> HasPermissionRouteAsync(HttpContext ctx, long id, long memberId, long value)
    {
        var db = ctx.GetDb();
        var channel = ctx.GetItem<PlanetVoiceChannel>(id);

        var targetMember = await FindAsync<PlanetMember>(memberId, db);
        if (targetMember is null)
            return ValourResult.NotFound<PlanetMember>();

        var hasPerm = await channel.HasPermissionAsync(targetMember, new Permission(value, "", ""), db);

        return Results.Json(hasPerm);
    }



    #endregion

    #region Validation

    /// <summary>
    /// Validates that a given name is allowable
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
            return new TaskResult(false, "Channel names must be 32 characters or less.");

        if (!nameRegex.IsMatch(name))
            return new TaskResult(false, "Channel names may only include letters, numbers, dashes, and underscores.");

        return new TaskResult(true, "The given name is valid.");
    }

    /// <summary>
    /// Validates that a given description is allowable
    /// </summary>
    public static TaskResult ValidateDescription(string desc)
    {
        if (desc.Length > 500)
        {
            return new TaskResult(false, "Channel descriptions must be 500 characters or less.");
        }

        return TaskResult.SuccessResult;
    }

    public static async Task<TaskResult> ValidateParentAndPosition(ValourDB db, PlanetVoiceChannel channel)
    {
        // Logic to check if parent is legitimate
        if (channel.ParentId is not null)
        {
            var parent = await db.PlanetCategoryChannels.FirstOrDefaultAsync
                (x => x.Id == channel.ParentId
                && x.PlanetId == channel.PlanetId); // This ensures the result has the same planet id

            if (parent is null)
                return new TaskResult(false, "Parent ID is not valid");
        }

        // Auto determine position
        if (channel.Position < 0)
        {
            channel.Position = (ushort)(await db.PlanetChannels.CountAsync(x => x.ParentId == channel.ParentId));
        }
        else
        {
            if (!await HasUniquePosition(db, channel))
                return new TaskResult(false, "The position is already taken.");
        }

        return new TaskResult(true, "Valid");
    }

    #endregion
}
