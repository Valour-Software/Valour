using Valour.Server.Database;
using Valour.Server.Requests;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class ChannelService
{
    private readonly ValourDB _db;
    private readonly PlanetService _planetService;
    private readonly PlanetCategoryService _categoryService;
    private readonly PlanetMemberService _memberService;
    private readonly ILogger<ChannelService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly PlanetRoleService _planetRoleService;

    public ChannelService(
        ValourDB db,
        PlanetService planetService,
        PlanetCategoryService categoryService,
        PlanetMemberService memberService,
        CoreHubService coreHubService,
        ILogger<ChannelService> logger,
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
    /// Returns the channel with the given id
    /// </summary>
    public async ValueTask<Channel> GetAsync(long id) =>
        (await _db.Channels.FindAsync(id)).ToModel();
    
    /// <summary>
    /// Soft deletes the given channel
    /// </summary>
    public async Task<TaskResult> DeleteAsync(Channel channel)
    {
        var dbChannel = await _db.Channels.FindAsync(channel.Id);
        if (dbChannel is null)
            return TaskResult.FromError( "Channel not found.");
        
        if (dbChannel.IsDefault == true)
            return TaskResult.FromError("You cannot delete the default channel.");
        
        dbChannel.IsDeleted = true;
        _db.Channels.Update(dbChannel);
        await _db.SaveChangesAsync();

        if (channel.PlanetId is not null)
            _coreHub.NotifyPlanetItemDelete(channel.PlanetId.Value, channel);
        
        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Creates the given channel
    /// </summary>
    public async Task<TaskResult<Channel>> CreateAsync(CreateChannelRequest request)
    {
        var channel = request.Channel;
        List<PermissionsNode> nodes = null;
        
        var baseValid = await ValidateChannel(channel);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        // Only planet channels have permission nodes
        if (channel.PlanetId is not null)
        {
            var member = await _memberService.GetCurrentAsync(channel.PlanetId.Value);
            if (member is null)
                return TaskResult<Channel>.FromError("You are not a member of this planet.");

            var authority = await _memberService.GetAuthorityAsync(member);
            
            // Handle bundled permissions
            nodes = new();
            if (request.Nodes is not null)
            {
                foreach (var node in request.Nodes)
                {
                    node.TargetId = channel.Id;
                    node.PlanetId = channel.PlanetId.Value;
                    
                    var role = await _planetRoleService.GetAsync(node.RoleId);
                    if (role.GetAuthority() > authority)
                        return TaskResult<Channel>.FromError(
                            "You have a lower authority than the permission node you are trying to create.");

                    node.Id = IdManager.Generate();
                }
            }
        }

        channel.Id = IdManager.Generate();

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            await _db.Channels.AddAsync(channel.ToDatabase());
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

            // Only add nodes if necessary
            if (nodes is not null)
            {
                await _db.PermissionsNodes.AddRangeAsync(nodes.Select(x => x.ToDatabase()));
                await _db.SaveChangesAsync();
            }

            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create planet chat channel");
            await tran.RollbackAsync();
            return TaskResult<Channel>.FromError("Failed to create channel");
        }

        if (channel.PlanetId is not null)
            _coreHub.NotifyPlanetItemChange(channel.PlanetId.Value, channel);

        return TaskResult<Channel>.FromData(channel);
    }
    
    /// <summary>
    /// Updates the given channel
    /// </summary>
    public async Task<TaskResult<Channel>> UpdateAsync(Channel updated)
    {
        var old = await _db.Channels.FindAsync(updated.Id);
        if (old is null) 
            return TaskResult<Channel>.FromError("Channel not found");
        
        // Update-specific validation
        if (old.Id != updated.Id)
            return TaskResult<Channel>.FromError("Cannot change Id.");
        
        if (old.PlanetId != updated.PlanetId)
            return TaskResult<Channel>.FromError("Cannot change PlanetId.");
        
        if (old.ChannelType != updated.ChannelType)
            return TaskResult<Channel>.FromError("Cannot change ChannelType.");

        // Basic validation
        var baseValid = await ValidateChannel(updated);
        if (!baseValid.Success)
            return TaskResult<Channel>.FromError(baseValid.Message);

        // Update
        try
        {
            _db.Entry(old).CurrentValues.SetValues(updated);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError("{Time}:{Error}", DateTime.UtcNow.ToShortTimeString(), e.Message);
            return new(false, e.Message);
        }

        if (updated.PlanetId is not null)
        {
            _coreHub.NotifyPlanetItemChange(updated.PlanetId.Value, updated);
        }

        // Response
        return TaskResult<Channel>.FromData(updated);
    }
    
    /// <summary>
    /// Sets the order of the children of a category. The order should contain all the children of the category.
    /// The list should contain the ids of the children in the order they should be displayed.
    /// </summary>
    public async Task<TaskResult> SetChildOrderAsync(long categoryId, List<long> order)
    {
        var category = await _db.Channels.FirstOrDefaultAsync(x =>
            x.Id == categoryId && x.ChannelType == ChannelTypeEnum.PlanetCategory);
        
        // Ensure that the category exists (and is actually a category)
        if (category is null)
            return TaskResult.FromError("Category not found.");
        
        // Prevent duplicates
        order = order.Distinct().ToList();
        
        var totalChildren = await _db.Channels.CountAsync(x => x.ParentId == categoryId);

        if (totalChildren != order.Count)
            return new(false, "Your order does not contain all the children.");

        // Use transaction so we can stop at any failure
        await using var tran = await _db.Database.BeginTransactionAsync();

        List<ChannelOrderData> newOrder = new();

        try
        {
            var pos = 0;
            foreach (var childId in order)
            {
                var child = await _db.Channels.FindAsync(childId);
                if (child is null)
                    return TaskResult.FromError($"Child with id {childId} does not exist!");

                if (child.ParentId != categoryId)
                    return new(false, $"Category {childId} is not a child of {categoryId}.");

                child.Position = pos;

                newOrder.Add(new(child.Id, child.ChannelType));

                pos++;
            }

            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError("{Time}:{Error}", DateTime.UtcNow.ToShortTimeString(), e.Message);
            return new(false, e.Message);
        }

        if (category.PlanetId is not null)
        {
            _coreHub.NotifyCategoryOrderChange(new()
            {
                PlanetId = category.PlanetId.Value,
                CategoryId = categoryId,
                Order = newOrder
            });
        }

        return new(true, "Success");
    }
    
    /// <summary>
    /// Returns the children of the given channel id
    /// </summary>
    public async Task<List<Channel>> GetChildrenAsync(long id) =>
        await _db.Channels.Where(x => x.ParentId == id)
            .OrderBy(x => x.Position)
            .Select(x => x.ToModel())
            .ToListAsync();

    /// <summary>
    /// Returns the number of children for the given channel id
    /// </summary>
    public async Task<int> GetChildCountAsync(long id) =>
        await _db.Channels.CountAsync(x => x.ParentId == id);

    /// <summary>
    /// Returns the ids of all of the children of the given channel id
    /// </summary>
    public async Task<List<long>> GetChildrenIdsAsync(long id) =>
        await _db.Channels.Where(x => x.ParentId == id)
            .Select(x => x.Id)
            .ToListAsync();
    
    /// <summary>
    /// Returns if the given category id is the last remaining category
    /// in its planet (used to prevent deletion of the last category)
    /// </summary>
    /// <param name="categoryId"></param>
    /// <returns></returns>
    public async Task<bool> IsLastCategory(long categoryId) =>
        await _db.Channels.CountAsync(x => x.PlanetId == categoryId && x.ChannelType == ChannelTypeEnum.PlanetCategory) < 2;
    
    
    
    #region Permissions
    
    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(Channel channel, PlanetMember member, CategoryPermission permission) =>
        await _memberService.HasPermissionAsync(member, channel, permission);
    
    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(Channel channel, PlanetMember member, ChatChannelPermission permission) =>
        await _memberService.HasPermissionAsync(member, channel, permission);
    
    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(Channel channel, PlanetMember member, VoiceChannelPermission permission) =>
        await _memberService.HasPermissionAsync(member, channel, permission);
    
    #endregion
    
    //////////////
    // Messages //
    //////////////
    
    public async Task<List<Message>> GetMessagesAsync(long channelId, int count = 50, long index = long.MaxValue)
    {
        var channel = await _db.Channels.FindAsync(channelId);
        if (channel is null)
            return null;
        
        if (!ISharedChannel.MessageChannelTypes.Contains(channel.ChannelType))
            return null;

        // Not sure why this request would even be made
        if (count < 1)
            return new();

        List<Message> staged = null;

        if (channel.ChannelType == ChannelTypeEnum.PlanetChat)
        {
            staged = PlanetMessageWorker.GetStagedMessages(channel.Id);
        }
        
        var messages = await _db.Messages.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Id < index)
            .Include(x => x.ReplyToMessage)
            .OrderByDescending(x => x.TimeSent)
            .Take(count)
            .Reverse()
            .Select(x => x.ToModel().AddReplyTo(x.ReplyToMessage.ToModel()))
            .ToListAsync();

        // Not all channels actually stage messages
        if (staged is not null)
        {
            messages.AddRange(staged);
        }

        return messages;
    }
    
    ////////////////
    // Validation //
    ////////////////
    
    /// <summary>
    /// The regex used for name validation
    /// </summary>
    public static readonly Regex NameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");
    
    /// <summary>
    /// Validates that a given name is allowable
    /// </summary>
    private TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
            return TaskResult.FromError("Channel names must be 32 characters or less.");

        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Validates that a given description is allowable
    /// </summary>
    private TaskResult ValidateDescription(string desc)
    {
        if (desc.Length > 500)
        {
            return TaskResult.FromError("Planet descriptions must be 500 characters or less.");
        }

        return TaskResult.SuccessResult;
    }
    
    
    /// <summary>
    /// Ensures the position is unique
    /// </summary>
    private async Task<bool> HasUniquePosition(Channel channel) =>
        // Ensure position is not already taken
        !await _db.Channels.AnyAsync(x => x.ParentId == channel.ParentId && // Same parent
                                                x.Position == channel.Position && // Same position
                                                x.Id != channel.Id); // Not self
    
    /// <summary>
    /// Ensures the parent and position are valid
    /// </summary>
    private async Task<TaskResult> ValidateParentAndPosition(Channel channel)
    {
        // Logic to check if parent is legitimate
        if (channel.ParentId is not null)
        {
            // Only planet channels can have a parent
            if (channel.PlanetId is null)
            {
                return TaskResult.FromError("Only planet channels can have a parent.");
            }
            
            var parent = await _db.Channels.FirstOrDefaultAsync
            (x => x.Id == channel.ParentId
                  && x.PlanetId == channel.PlanetId // This ensures the result has the same planet id
                  && x.ChannelType == ChannelTypeEnum.PlanetCategory); // Only categories can be parents 

            if (parent is null)
                return TaskResult.FromError( "Parent channel not found");
            
            if (parent.Id == channel.Id)
                return TaskResult.FromError( "A channel cannot be its own parent.");

            // Ensure that the channel is not a descendant of itself
            var loopScan = parent;
            
            while (loopScan.ParentId is not null)
            {
                if (loopScan.ParentId == channel.Id)
                    return TaskResult.FromError( "A channel cannot be a descendant of itself.");
                
                loopScan = await _db.Channels.FirstOrDefaultAsync(x => x.Id == loopScan.ParentId);
            }
        }

        // Auto determine position
        if (channel.Position < 0)
        {
            channel.Position = await _db.Channels.CountAsync(x => x.ParentId == channel.ParentId);
        }
        else
        {
            if (!await HasUniquePosition(channel))
                return TaskResult.FromError( "The position is already taken.");
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates the planet of a channel
    /// </summary>
    private async Task<TaskResult> ValidatePlanet(Channel channel)
    {
        if (ISharedChannel.PlanetChannelTypes.Contains(channel.ChannelType))
        {
            if (!await _db.Planets.AnyAsync(x => x.Id == channel.PlanetId))
            {
                return TaskResult.FromError("Planet not found.");
            }
        }
        else
        {
            if (channel.PlanetId is not null)
            {
                return TaskResult.FromError("Only planet channel types can have a planet id.");
            } 
        }

        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Common basic validation for channels
    /// </summary>
    private async Task<TaskResult> ValidateChannel(Channel channel)
    {
        var planetValid = await ValidatePlanet(channel);
        if (!planetValid.Success)
            return planetValid;
        
        var nameValid = ValidateName(channel.Name);
        if (!nameValid.Success)
            return nameValid;

        var descValid = ValidateDescription(channel.Description);
        if (!descValid.Success)
            return descValid;

        var positionValid = await ValidateParentAndPosition(channel);
        if (!positionValid.Success)
            return positionValid;

        return TaskResult.SuccessResult;
    }
}