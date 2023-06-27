using Valour.Server.Database;
using Valour.Server.Requests;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetChatChannelService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetCategoryService _categoryService;
    private readonly PlanetMemberService _memberService;
    private readonly ILogger<PlanetChatChannelService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly PlanetRoleService _planetRoleService;

    public PlanetChatChannelService(
        ValourDB db,
        PlanetService planetService,
        PlanetCategoryService categoryService,
        PlanetMemberService memberService,
        CoreHubService coreHubService,
        ILogger<PlanetChatChannelService> logger,
        PlanetRoleService planetRoleService)
    {
        _db = db;
        _planetService = planetService;
        _categoryService = categoryService;
        _memberService = memberService;
        _logger = logger;
        _coreHub = coreHubService;
        _planetRoleService = planetRoleService;
    }

    /// <summary>
    /// Returns the chat channel with the given id
    /// </summary>
    public async ValueTask<PlanetChatChannel> GetAsync(long id) =>
        (await _db.PlanetChatChannels.FindAsync(id)).ToModel();


    /// <summary>
    /// Soft deletes the given channel
    /// </summary>
    public async Task<TaskResult> DeleteAsync(PlanetChatChannel channel)
    {
        var dbChannel = await _db.PlanetChatChannels.FindAsync(channel.Id);
        if (dbChannel.IsDefault)
            return new TaskResult(false, "You cannot delete the default channel.");
        
        dbChannel.IsDeleted = true;
        _db.PlanetChatChannels.Update(dbChannel);
        await _db.SaveChangesAsync();

        _coreHub.NotifyPlanetItemDelete(channel);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Creates the given planet chat channel
    /// </summary>
    public async Task<TaskResult<PlanetChatChannel>> CreateAsync(PlanetChatChannel channel)
    {
        var baseValid = await ValidateBasic(channel);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        channel.Id = IdManager.Generate();

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            await _db.PlanetChatChannels.AddAsync(channel.ToDatabase());
            await _db.SaveChangesAsync();

            // Add fresh channel state
            var state = new Valour.Database.ChannelState()
            {
                ChannelId = channel.Id,
                PlanetId = channel.PlanetId,
                LastUpdateTime = DateTime.UtcNow,
            };

            await _db.ChannelStates.AddAsync(state);
            await _db.SaveChangesAsync();
                
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create planet chat channel");
            await tran.RollbackAsync();
            return new(false, "Failed to create channel");
        }

        _coreHub.NotifyPlanetItemChange(channel);

        return new(true, "PlanetChatChannel created successfully", channel);
    }

    /// <summary>
    /// Creates the given planet chat channel
    /// </summary>
    public async Task<TaskResult<PlanetChatChannel>> CreateDetailedAsync(CreatePlanetChatChannelRequest request, PlanetMember member)
    {
        var channel = request.Channel;
        List<PermissionsNode> nodes = new();

        channel.Id = IdManager.Generate();

        // Create nodes
        foreach (var nodeReq in request.Nodes) {
            var node = nodeReq;
            node.TargetId = channel.Id;
            node.PlanetId = channel.PlanetId;

            var role = await _planetRoleService.GetAsync(node.RoleId);
            if (role.GetAuthority() > await _memberService.GetAuthorityAsync(member))
                return new(true, "A permission node's role has higher authority than you.");

            node.Id = IdManager.Generate();

            nodes.Add(node);
        }

        var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            await _db.PlanetChatChannels.AddAsync(channel.ToDatabase());
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

    public async Task<TaskResult<PlanetChatChannel>> UpdateAsync(PlanetChatChannel updated)
    {
        var old = await _db.PlanetChatChannels.FindAsync(updated.Id);
        if (old is null) return new(false, $"PlanetChatChannel not found");
        // Validation
        if (old.Id != updated.Id)
            return new(false, "Cannot change Id.");
        if (old.PlanetId != updated.PlanetId)
            return new(false, "Cannot change PlanetId.");

        var baseValid = await ValidateBasic(updated);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        // Update
        try
        {
            _db.Entry(old).CurrentValues.SetValues(updated);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        _coreHub.NotifyPlanetItemChange(updated);

        // Response
        return new(true, "Success", updated);
    }

    /// <summary>
    /// Common basic validation for channels
    /// </summary>
    private async Task<TaskResult> ValidateBasic(PlanetChatChannel channel)
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
    /// The regex used for name validation
    /// </summary>
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

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
            return new TaskResult(false, "Planet descriptions must be 500 characters or less.");
        }

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> ValidateParentAndPosition(PlanetChatChannel channel)
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

    public async Task<List<MessageTransferData<PlanetMessage>>> GetMessagesAsync(PlanetChatChannel channel, int count = 50, long index = long.MaxValue)
    {
        // Not sure why this request would even be made
        if (count < 1)
            return new();
        
        List<PlanetMessage> staged = PlanetMessageWorker.GetStagedMessages(channel.Id);
        
        var messages = await _db.PlanetMessages.Where(x => x.ChannelId == channel.Id && x.Id < index)
                                              .Include(x => x.ReplyToMessage)
                                              .OrderByDescending(x => x.TimeSent)
                                              .Take(count)
                                              .Reverse()
                                              .Select(x => new MessageTransferData<PlanetMessage>()
                                              {
                                                  Message = x.ToModel(),
                                                  Reply = x.ReplyToMessage.ToModel()
                                              })
                                              .ToListAsync();

        if (staged.Count > 0)
        {
            List<MessageTransferData<PlanetMessage>> stagedData = new();
            foreach (var msg in staged)
            {
                PlanetMessage reply = null;
                if (msg.ReplyToId is not null)
                {
                    reply = (await _db.PlanetMessages.FindAsync(msg.ReplyToId)).ToModel();
                }
                
                stagedData.Add(new MessageTransferData<PlanetMessage>()
                {
                    Message = msg,
                    Reply = reply
                });
            }
        }
        
        return messages;
    }
}