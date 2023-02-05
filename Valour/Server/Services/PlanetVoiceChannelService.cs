using Valour.Server.Database;
using Valour.Server.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;

namespace Valour.Server.Services;

public class PlanetVoiceChannelService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetMemberService _planetMemberService;
    private readonly PlanetRoleService _planetRoleService;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<PlanetVoiceChannelService> _logger;

    public PlanetVoiceChannelService(
        ValourDB db,
        PlanetService planetService,
        PlanetMemberService planetMemberService,
        CoreHubService coreHub,
        PlanetRoleService planetRoleService,
        ILogger<PlanetVoiceChannelService> logger)
    {
        _db = db;
        _planetService = planetService;
        _planetMemberService = planetMemberService;
        _coreHub = coreHub;
        _logger = logger;
        _planetRoleService = planetRoleService;
    }

    /// <summary>
    /// Returns the voice channel with the given id
    /// </summary>
    public async ValueTask<PlanetVoiceChannel> GetAsync(long id) =>
        (await _db.PlanetVoiceChannels.FindAsync(id)).ToModel();

    public async Task<TaskResult<PlanetVoiceChannel>> CreateAsync(PlanetVoiceChannel channel)
    {
        var baseValid = await ValidateBasic(channel);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        channel.Id = IdManager.Generate();

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            await _db.PlanetVoiceChannels.AddAsync(channel.ToDatabase());
            await _db.SaveChangesAsync();

            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create planet voice channel");
            await tran.RollbackAsync();
            return new(false, "Failed to create channel");
        }

        _coreHub.NotifyPlanetItemChange(channel);

        return new(true, "PlanetVoiceChannel created successfully", channel);
    }

    /// <summary>
    /// Creates the given planet chat channel
    /// </summary>
    public async Task<TaskResult<PlanetVoiceChannel>> CreateDetailedAsync(CreatePlanetVoiceChannelRequest request, PlanetMember member)
    {
        var channel = request.Channel;
        List<PermissionsNode> nodes = new();

        channel.Id = IdManager.Generate();

        // Create nodes
        foreach (var nodeReq in request.Nodes)
        {
            var node = nodeReq;
            node.TargetId = channel.Id;
            node.PlanetId = channel.PlanetId;

            var role = await _planetRoleService.GetAsync(node.RoleId);
            if (role.GetAuthority() > await _planetMemberService.GetAuthorityAsync(member))
                return new(true, "A permission node's role has higher authority than you.");

            node.Id = IdManager.Generate();

            nodes.Add(node);
        }

        var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            await _db.PlanetVoiceChannels.AddAsync(channel.ToDatabase());
            await _db.SaveChangesAsync();

            await _db.PermissionsNodes.AddRangeAsync(nodes.Select(x => x.ToDatabase()));
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(true, e.Message);
        }

        await tran.CommitAsync();

        _coreHub.NotifyPlanetItemChange(channel);

        return new(true, "Success", channel);
    }

    public async Task<TaskResult<PlanetVoiceChannel>> PutAsync(PlanetVoiceChannel updatedchannel)
    {
        var old = await _db.PlanetVoiceChannels.FindAsync(updatedchannel.Id);
        if (old is null) return new(false, $"PlanetVoiceChannel not found");

        // Validation
        if (old.Id != updatedchannel.Id)
            return new(false, "Cannot change Id.");
        if (old.PlanetId != updatedchannel.PlanetId)
            return new(false, "Cannot change PlanetId.");

        var baseValid = await ValidateBasic(updatedchannel);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        // Update
        try
        {
            _db.Entry(old).CurrentValues.SetValues(updatedchannel);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(updatedchannel);

        // Response
        return new(true, "Success", updatedchannel);
    }

    public async Task<TaskResult> DeleteAsync(PlanetVoiceChannel channel)
    {
        // Always use transaction for multi-step DB operations
        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // Remove permission nodes
            _db.PermissionsNodes.RemoveRange(
                _db.PermissionsNodes.Where(x => x.TargetId == channel.Id)
            );

            // Remove channel
            _db.PlanetVoiceChannels.Remove(await _db.PlanetVoiceChannels.FindAsync(channel.Id));
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            await transaction.RollbackAsync();
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemDelete(channel);

        return new(true, "Success");
    }

    /// <summary>
    /// The regex used for name validation
    /// </summary>
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

    /// <summary>
    /// Returns all members who can see this channel
    /// </summary>
    public async Task<List<PlanetMember>> GetChannelMembersAsync(PlanetVoiceChannel channel)
    {
        // TODO: It would be more efficient to check each role and then get all users in those roles
        // rather than going member by member. Revisit this later.

        List<PlanetMember> members = new();

        var planetMembers = _db.PlanetMembers.Include(x => x.RoleMembership).Where(x => x.PlanetId == channel.PlanetId);

        foreach (var member in planetMembers)
        {
            if (await _planetMemberService.HasPermissionAsync(member.ToModel(), channel, VoiceChannelPermissions.View))
                members.Add(member.ToModel());
        }

        return members;
    }

    /// <summary>
    /// Common basic validation for voice channels
    /// </summary>
    private async Task<TaskResult> ValidateBasic(PlanetVoiceChannel channel)
    {
        var nameValid = ValidateName(channel.Name);
        if (!nameValid.Success)
            return new TaskResult(false, nameValid.Message);

        var descValid = ValidateDescription(channel.Description);
        if (!descValid.Success)
            return new TaskResult(false, nameValid.Message);

        var positionValid = await ValidateParentAndPosition(channel);
        if (!positionValid.Success)
            return new TaskResult(false, nameValid.Message);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates that a given name is allowable
    /// </summary>
    public TaskResult ValidateName(string name)
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
    public TaskResult ValidateDescription(string desc)
    {
        if (desc.Length > 500)
            return new TaskResult(false, "Channel descriptions must be 500 characters or less.");

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> ValidateParentAndPosition(PlanetVoiceChannel channel)
    {
        // Logic to check if parent is legitimate
        if (channel.ParentId is not null)
        {
            var parent = await _db.PlanetCategories.FirstOrDefaultAsync
                (x => x.Id == channel.ParentId
                && x.PlanetId == channel.PlanetId); // This ensures the result has the same planet id

            if (parent is null)
                return new TaskResult(false, "Parent ID is not valid");
        }

        // Auto determine position
        if (channel.Position < 0)
        {
            channel.Position = (ushort)(await _db.PlanetChannels.CountAsync(x => x.ParentId == channel.ParentId));
        }
        else
        {
            if (!await HasUniquePosition(channel))
                return new TaskResult(false, "The position is already taken.");
        }

        return new TaskResult(true, "Valid");
    }

    public async Task<bool> HasUniquePosition(PlanetChannel channel) =>
        // Ensure position is not already taken
        !await _db.PlanetChannels.AnyAsync(x => x.ParentId == channel.ParentId && // Same parent
                                                x.Position == channel.Position && // Same position
                                                x.Id != channel.Id); // Not self
}