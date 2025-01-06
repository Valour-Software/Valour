#nullable enable

using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class ChannelService
{
    private readonly ValourDb _db;
    private readonly PlanetMemberService _memberService;
    private readonly ILogger<ChannelService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly PlanetPermissionService _planetPermissionService;
    private readonly HostedPlanetService _hostedPlanetService;

    public ChannelService(
        ValourDb db,
        PlanetMemberService memberService,
        CoreHubService coreHubService,
        ILogger<ChannelService> logger,
        PlanetPermissionService planetPermissionService, 
        HostedPlanetService hostedPlanetService)
    {
        _db = db;
        _memberService = memberService;
        _logger = logger;
        _coreHub = coreHubService;
        _planetPermissionService = planetPermissionService;
        _hostedPlanetService = hostedPlanetService;
    }

    /// <summary>
    /// Returns the channel with the given id
    /// </summary>
    public async ValueTask<Channel?> GetPlanetChannelAsync(long planetId, long channelId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        var channel = hostedPlanet.GetChannel(channelId);
        return channel;
    }

    /// <summary>
    /// Given two user ids, returns the direct chat channel between them
    /// </summary>
    public async ValueTask<Channel?> GetDirectChatAsync(long userOneId, long userTwoId, bool create = true)
    {
        var channel = await _db.Channels
            .AsNoTracking()
            .Include(x => x.Members)
            .Where(x => x.ChannelType == ChannelTypeEnum.DirectChat)
            .Where(x => x.Members.Any(m => m.UserId == userOneId) &&
                        x.Members.Any(m => m.UserId == userTwoId))
            .FirstOrDefaultAsync();

        // If there is no channel and we have this set to create it if missing...
        if (channel is null && create)
        {
            var newId = IdManager.Generate();
            
            // Create channel
            channel = new Valour.Database.Channel()
            {
                Id = newId,

                // Build the members
                Members = [
                    new()
                    {
                        Id = IdManager.Generate(),
                        ChannelId = newId,
                        UserId = userOneId
                    },
                    new()
                    {
                        Id = IdManager.Generate(),
                        ChannelId = newId,
                        UserId = userTwoId
                    }
                ],

                Name = "Direct Chat",
                Description = "A private discussion",
                ChannelType = ChannelTypeEnum.DirectChat,
                LastUpdateTime = DateTime.UtcNow,
                IsDeleted = false,
                
                RawPosition = 0,
                InheritsPerms = false,
                IsDefault = false,

                // These are null and technically we don't have to show this
                // but I am showing it so you know it SHOULD be null!
                PlanetId = null,
                ParentId = null,
            };
            
            await _db.Channels.AddAsync(channel);
            await _db.SaveChangesAsync();
        }
        
        return channel?.ToModel();
    }

    /// <summary>
    /// Returns all the direct chat channels for the given user id
    /// </summary>
    public Task<List<Channel>> GetAllDirectAsync(long userId)
    {
        return _db.Channels.Include(x => x.Members)
            .Where(x => x.ChannelType == ChannelTypeEnum.DirectChat &&
                        x.Members.Any(m => m.UserId == userId))
            .Select(x => x.ToModel())
            .ToListAsync();
    }
    
    /// <summary>
    /// Soft deletes the given channel
    /// </summary>
    public async Task<TaskResult> DeleteAsync(long id)
    {
        var dbChannel = await _db.Channels.FindAsync(id);
        if (dbChannel is null)
            return TaskResult.FromFailure( "Channel not found.");

        HostedPlanet? hostedPlanet = null;
        if (dbChannel.PlanetId is not null)
        {
            hostedPlanet = await _hostedPlanetService.GetRequiredAsync(dbChannel.PlanetId.Value);
        } 
        
        if (dbChannel.IsDefault == true)
            return TaskResult.FromFailure("You cannot delete the default channel.");
        
        dbChannel.IsDeleted = true;
        _db.Channels.Update(dbChannel);
        await _db.SaveChangesAsync();
        
        // Remove from hosted planet
        if (hostedPlanet is not null)
        {
            hostedPlanet.RemoveChannel(dbChannel.Id);
        }
        
        var model = dbChannel.ToModel();

        if (model.PlanetId is not null)
            _coreHub.NotifyPlanetItemDelete(model.PlanetId.Value, model);
        
        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Creates the given channel
    /// </summary>
    public async Task<TaskResult<Channel>> CreateAsync(Channel channel, List<PermissionsNode> nodes = null)
    {
        var baseValid = await ValidateChannel(channel);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        if (ISharedChannel.PlanetChannelTypes.Contains(channel.ChannelType))
        {
            if (channel.PlanetId is null)
            {
                return TaskResult<Channel>.FromFailure("PlanetId is required for planet channels.");
            }
        }
        
        HostedPlanet? hostedPlanet = null;

        // Only planet channels have permission nodes
        if (channel.PlanetId is not null)
        {
            hostedPlanet = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
            
            // Handle bundled permissions
            if (nodes is not null && nodes.Count > 0)
            {
                foreach (var node in nodes)
                {
                    if (node.TargetId != channel.Id)
                        return TaskResult<Channel>.FromFailure("Node target id does not match channel id");
                
                    if (node.PlanetId != channel.PlanetId)
                        return TaskResult<Channel>.FromFailure("Node planet id does not match channel planet id");
                    
                    node.Id = IdManager.Generate();
                }
            }
        }
        else
        {
            if (channel.ParentId is not null)
            {
                return TaskResult<Channel>.FromFailure("Only planet channels can have a parent.");
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
            return TaskResult<Channel>.FromFailure("Failed to create channel");
        }

        if (hostedPlanet is not null)
        {
            hostedPlanet.UpsertChannel(channel);
            _coreHub.NotifyPlanetItemChange(channel.PlanetId!.Value, channel);
        }

        return TaskResult<Channel>.FromData(channel);
    }
    
    /// <summary>
    /// Updates the given channel
    /// </summary>
    public async Task<TaskResult<Channel>> UpdateAsync(Channel updated)
    {
        var old = await _db.Channels.FindAsync(updated.Id);
        if (old is null) 
            return TaskResult<Channel>.FromFailure("Channel not found");
        
        // Update-specific validation
        if (old.Id != updated.Id)
            return TaskResult<Channel>.FromFailure("Cannot change Id.");
        
        if (old.PlanetId != updated.PlanetId)
            return TaskResult<Channel>.FromFailure("Cannot change PlanetId.");
        
        if (old.ChannelType != updated.ChannelType)
            return TaskResult<Channel>.FromFailure("Cannot change ChannelType.");

        // Channel parent is being changed
        if (old.ParentId != updated.ParentId)
        {
            return TaskResult<Channel>.FromFailure("Use the order endpoint in the parent category to update parent.");
        }
        // Channel is being moved
        if (old.RawPosition != updated.RawPosition)
        {
            return TaskResult<Channel>.FromFailure("Use the order endpoint in the parent category to change position.");
        }
        
        // Basic validation
        var baseValid = await ValidateChannel(updated);
        if (!baseValid.Success)
            return TaskResult<Channel>.FromFailure(baseValid.Message);

        HostedPlanet? hostedPlanet = null;
        if (updated.PlanetId is not null)
        {
            hostedPlanet = await _hostedPlanetService.GetRequiredAsync(updated.PlanetId.Value);
        }

        var trans = await _db.Database.BeginTransactionAsync();
        
        // Update
        try
        {
            _db.Entry(old).CurrentValues.SetValues(updated);
            await _db.SaveChangesAsync();

            await trans.CommitAsync();
        }
        catch (System.Exception e)
        {
            await trans.RollbackAsync();
            _logger.LogError("{Time}:{Error}", DateTime.UtcNow.ToShortTimeString(), e.Message);
            return new(false, e.Message);
        }

        if (hostedPlanet is not null)
        {
            hostedPlanet.UpsertChannel(updated);
            _coreHub.NotifyPlanetItemChange(updated.PlanetId!.Value, updated);
        }

        // Response
        return TaskResult<Channel>.FromData(updated);
    }
    
    /// <summary>
    /// Sets the order of the children of a category. The order should contain all the children of the category.
    /// The list should contain the ids of the children in the order they should be displayed.
    /// </summary>
    public async Task<TaskResult> SetChildOrderAsync(long planetId, long? categoryId, List<long> order)
    {
        var category = await _db.Channels.FirstOrDefaultAsync(x =>
            x.Id == categoryId && x.ChannelType == ChannelTypeEnum.PlanetCategory);
        
        // Ensure that the category exists (and is actually a category)
        if (category is null)
            return TaskResult.FromFailure("Category not found.");
        
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
            uint pos = 0;
            foreach (var childId in order)
            {
                var child = await _db.Channels.FindAsync(childId);
                if (child is null)
                    return TaskResult.FromFailure($"Child with id {childId} does not exist!");

                if (child.ParentId != categoryId)
                    return new(false, $"Category {childId} is not a child of {categoryId}.");

                child.RawPosition = pos;

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
                PlanetId = planetId,
                CategoryId = categoryId,
                Order = newOrder
            });
        }

        return new(true, "Success");
    }
    
    /// <summary>
    /// Returns the children of the given channel id
    /// </summary>
    public Task<List<Channel>> GetChildrenAsync(long id) =>
        _db.Channels.Where(x => x.ParentId == id)
            .OrderBy(x => x.RawPosition)
            .Select(x => x.ToModel())
            .ToListAsync();

    /// <summary>
    /// Returns the number of children for the given channel id
    /// </summary>
    public Task<int> GetChildCountAsync(long id) =>
        _db.Channels.CountAsync(x => x.ParentId == id);

    /// <summary>
    /// Returns the ids of all of the children of the given channel id
    /// </summary>
    public Task<List<long>> GetChildrenIdsAsync(long id) =>
        _db.Channels.Where(x => x.ParentId == id)
            .Select(x => x.Id)
            .ToListAsync();
    
    /// <summary>
    /// Returns if the given category id is the last remaining category
    /// in its planet (used to prevent deletion of the last category)
    /// </summary>
    public async Task<bool> IsLastCategory(long categoryId) =>
        await _db.Channels.CountAsync(x => x.PlanetId == categoryId && x.ChannelType == ChannelTypeEnum.PlanetCategory) < 2;

    /// <summary>
    /// Returns if the given user id is a member of the given channel id
    /// </summary>
    public async ValueTask<bool> IsMemberAsync(long channelId, long userId)
        => await IsMemberAsync(await GetPlanetChannelAsync(channelId), userId);
    
    /// <summary>
    /// Returns if the given user id is a member of the given channel
    /// </summary>
    public async Task<bool> IsMemberAsync(Channel channel, long userId)
    {
        // Direct messages access
        if (channel.PlanetId is null)
        {
            return await _db.ChannelMembers.AnyAsync(x => x.ChannelId == channel.Id && x.UserId == userId);
        }
        // Planet channel access
        else
        {
            return await _db.MemberChannelAccess.AnyAsync(x => x.ChannelId == channel.Id && x.UserId == userId);
        }
    }
    
    /// <summary>
    /// Returns the channel members for a given channel id,
    /// which is NOT planet members
    /// </summary>
    public Task<List<User>> GetMembersNonPlanetAsync(long channelId)
    {
        return _db.ChannelMembers.Include(x => x.User)
            .Where(x => x.ChannelId == channelId)
            .Select(x => x.User.ToModel())
            .ToListAsync();
    }
    
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

    /// <summary>
    /// Returns the permissions nodes for the given channel id
    /// </summary>
    public async Task<List<PermissionsNode>> GetPermissionNodesAsync(long channelId) =>
        await _db.PermissionsNodes.Where(x => x.TargetId == channelId)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    #endregion
    
    /// <summary>
    /// Returns all the descendents for a given channel position
    /// </summary>
    public async Task<List<Channel>> GetAllDescendants(Channel channel)
    {
        if (channel.ChannelType != ChannelTypeEnum.PlanetCategory)
            return new();
        
        // This works because position values contain the parent information.
        
        // First, get the depth of the channel
        var depth = channel.Position.Depth;
        
        // If depth is 4, it can't have children
        // (Technically you shouldn't even be able to have a category at level 4)
        if (depth == 4)
            return new();
        
        var descendants = await _db.Channels
            .DescendantsOf(channel)
            .Select(x => x.ToModel())
            .ToListAsync();
        
        return descendants;
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
    private static TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
            return TaskResult.FromFailure("Channel names must be 32 characters or less.");

        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Validates that a given description is allowable
    /// </summary>
    private static TaskResult ValidateDescription(string desc)
    {
        if (desc.Length > 500)
        {
            return TaskResult.FromFailure("Planet descriptions must be 500 characters or less.");
        }

        return TaskResult.SuccessResult;
    }
    
    
    /// <summary>
    /// Ensures the position is unique
    /// </summary>
    private async Task<bool> HasUniquePosition(Channel channel) =>
        // Ensure position is not already taken
        // Note: with new position system, we need to check the position and parent separately
        !await _db.Channels.AnyAsync(x => (x.ParentId == channel.ParentId || // Same parent
                                                x.RawPosition == channel.RawPosition) && // Same position
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
                return TaskResult.FromFailure("Only planet channels can have a parent.");
            }
            
            var parent = await _db.Channels.FirstOrDefaultAsync
            (x => x.Id == channel.ParentId
                  && x.PlanetId == channel.PlanetId // This ensures the result has the same planet id
                  && x.ChannelType == ChannelTypeEnum.PlanetCategory); // Only categories can be parents 

            if (parent is null)
                return TaskResult.FromFailure( "Parent channel not found");
            
            if (parent.Id == channel.Id)
                return TaskResult.FromFailure( "A channel cannot be its own parent.");

            // Ensure that the channel is not a descendant of itself
            var loopScan = parent;
            
            while (loopScan.ParentId is not null)
            {
                if (loopScan.ParentId == channel.Id)
                    return TaskResult.FromFailure( "A channel cannot be a descendant of itself.");
                
                loopScan = await _db.Channels.FirstOrDefaultAsync(x => x.Id == loopScan.ParentId);
            }
        }

        // Auto determine position
        if (channel.RawPosition < 0)
        {
            var nextPosResult = await TryGetNextChannelPosition(channel.PlanetId, channel.ParentId, channel.ChannelType);
            if (!nextPosResult.Success)
                return nextPosResult.WithoutData();
            
            channel.RawPosition = nextPosResult.Data;
        }
        else
        {
            if (!await HasUniquePosition(channel))
                return TaskResult.FromFailure( "The position is already taken.");
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
                return TaskResult.FromFailure("Planet not found.");
            }
        }
        else
        {
            if (channel.PlanetId is not null)
            {
                return TaskResult.FromFailure("Only planet channel types can have a planet id.");
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
    
    public async Task<TaskResult<uint>> TryGetNextChannelPosition(long? planetId, long? parentId, ChannelTypeEnum channelType)
    {
        var parent = (await _db.Channels.FindAsync(parentId)).ToModel();
        if (parent is null)
            return TaskResult<uint>.FromFailure("Parent channel not found");
        
        return await TryGetNextChannelPosition(planetId, parent, channelType);
    }

    public async Task<TaskResult<uint>> TryGetNextChannelPosition(long? planetId, Channel parentCategory, ChannelTypeEnum channelType)
    {
        // non-planet channels do not have a position
        if (planetId is null)
            return TaskResult<uint>.FromData(0);
        
        // position within the parent
        var parentPosition = 0u;
        
        if (parentCategory is not null)
        {
            var parentDepth = 0u;
            
            // you can't place a channel under a non-category
            if (parentCategory.ChannelType != ChannelTypeEnum.PlanetCategory)
                return TaskResult<uint>.FromData(0);
            
            parentDepth = parentCategory.Position.Depth;
            
            if (channelType == ChannelTypeEnum.PlanetCategory)
            {
                // We don't allow categories at greater than depth 2
                // because they wouldn't be able to contain anything
                if (parentDepth > 1) 
                {
                    return TaskResult<uint>.FromFailure("Max category depth reached (2)");
                }
            }
            else
            {
                if (parentDepth > 2)
                {
                    return TaskResult<uint>.FromFailure("Max channel depth reached (3)");
                }
            }
        }
        
        // Get the child with the highest position within the bounds
        var highestChildRawPosition = await _db.Channels
            .DirectChildrenOf(planetId, parentPosition)
            .Select(x => x.RawPosition)
            .DefaultIfEmpty(0u)
            .MaxAsync();

        var highestChildPosition = new ChannelPosition(highestChildRawPosition);
        
        if (highestChildPosition.LocalPosition > 249)
        {
            return TaskResult<uint>.FromFailure("Max category children reached");
        }
        
        // Add one to the highest child's relative position to get the new local position
        var newLocalPosition = highestChildPosition.LocalPosition + 1;
        
        // Stick the new local position onto the parent position to get the new position
        var newPosition =  ChannelPosition.AppendRelativePosition(parentPosition, newLocalPosition);
        
        return TaskResult<uint>.FromData(newPosition);
    }
}