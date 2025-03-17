﻿#nullable enable

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
    private readonly ChatCacheService _chatCacheService;

    public ChannelService(
        ValourDb db,
        PlanetMemberService memberService,
        CoreHubService coreHubService,
        ILogger<ChannelService> logger,
        PlanetPermissionService planetPermissionService, 
        HostedPlanetService hostedPlanetService, 
        ChatCacheService chatCacheService)
    {
        _db = db;
        _memberService = memberService;
        _logger = logger;
        _coreHub = coreHubService;
        _planetPermissionService = planetPermissionService;
        _hostedPlanetService = hostedPlanetService;
        _chatCacheService = chatCacheService;
    }

    /// <summary>
    /// Returns the channel with the given id
    /// </summary>
    public async ValueTask<Channel?> GetChannelAsync(long? planetId, long channelId)
    {
        if (planetId is not null)
        {
            var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId.Value);
            var channel = hostedPlanet.GetChannel(channelId);
            return channel;
        }
        else
        {
            var channel = await _db.Channels.FindAsync(channelId);
            if (channel is null)
                return null;
            
            // Require planet up front
            if (channel.PlanetId is not null)
                return null;
        
            return channel?.ToModel();
        }
    }

    /// <summary>
    /// Given two user ids, returns the direct chat channel between them
    /// </summary>
    public async ValueTask<Channel?> GetDirectChannelByUsersAsync(long userOneId, long userTwoId, bool create = true)
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
                
                LocalPosition = 0,
                InheritsPerms = false,
                IsDefault = false,

                // These are null and technically we don't have to show this
                // but I am showing it so you know it SHOULD be null!
                PlanetId = null,
                ParentId = null,
                
                Version = ISharedChannel.CurrentVersion,
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
    /// Soft deletes the given planet channel
    /// </summary>
    public async Task<TaskResult> DeletePlanetChannelAsync(long planetId, long channelId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
        
        var dbChannel = await _db.Channels.FindAsync(channelId);
        if (dbChannel is null)
            return TaskResult.FromFailure( "Channel not found.");
        
        if (dbChannel.PlanetId != planetId)
            return TaskResult.FromFailure( "Channel does not belong to planet.");
        
        if (dbChannel.IsDefault == true)
            return TaskResult.FromFailure("You cannot delete the default channel.");
        
        dbChannel.IsDeleted = true;
        _db.Channels.Update(dbChannel);
        await _db.SaveChangesAsync();
        
        // Remove from hosted planet
        hostedPlanet.RemoveChannel(dbChannel.Id);

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
        channel.LastUpdateTime = DateTime.UtcNow;

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            await _db.Channels.AddAsync(channel.ToDatabase());
            await _db.SaveChangesAsync();
            
            // Only add nodes if necessary
            if (nodes is not null)
            {
                foreach (var node in nodes)
                {
                    node.TargetId = channel.Id;
                }
                
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
            return TaskResult<Channel>.FromFailure("Use move channel endpoint to change parent.");
        }
        // Channel is being moved
        if (old.RawPosition != updated.RawPosition)
        {
            return TaskResult<Channel>.FromFailure("Use move channel endpoint to change position.");
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
    
    public async Task<bool> HasAccessAsync(Channel channel, long userId)
    {
        if (channel.PlanetId is not null)
        {
            var member = await _memberService.GetCurrentAsync(channel.PlanetId.Value);
            if (member is null)
                return false;
            
            return await _planetPermissionService.HasChannelAccessAsync(member.Id, channel.Id);
        }
        
        return await _db.ChannelMembers.AnyAsync(x => x.ChannelId == channel.Id && x.UserId == userId);
    }
    
    /// <summary>
    /// Returns the channel members for a given channel id,
    /// which is NOT planet members
    /// </summary>
    public Task<List<User>> GetDirectChannelMembersAsync(long channelId)
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
    /// Returns the permissions for a given member in a channel
    /// </summary>
    public ValueTask<long> GetPermissionsAsync(Channel channel, PlanetMember member, ChannelTypeEnum permType) =>
        _planetPermissionService.GetChannelPermissionsAsync(member, channel, permType);

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
        !await _db.Channels.AnyAsync(x => (x.ParentId == channel.ParentId && // Same parent
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
            
            while (loopScan!.ParentId is not null)
            {
                if (loopScan.ParentId == channel.Id)
                    return TaskResult.FromFailure("A channel cannot be a descendant of itself.");

                loopScan = await _db.Channels.FirstOrDefaultAsync(x => x.Id == loopScan.ParentId);
            }
            
        }

        // Auto determine position
        if (channel.RawPosition == 0)
        {
            var nextPosResult = await TryGetNextChannelLocalPosition(channel.PlanetId, channel.ParentId, channel.ChannelType);
            if (!nextPosResult.Success)
                return nextPosResult.WithoutData();
            
            channel.LocalPosition = nextPosResult.Data;
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

    public async Task<TaskResult<byte>> TryGetNextChannelLocalPosition(long planetId, long? parentId, bool isCategory)
    {
        var parent = parentId is null ? null : await _db.Channels.FindAsync(parentId);
        
        if (parent is not null)
        {
            // Ensure the parent is a category
            if (parent.ChannelType != ChannelTypeEnum.PlanetCategory)
            {
                return TaskResult<byte>.FromFailure("Parent is not a category.");
            }
            
            // If the channel being inserted is a category, ensure that it's not too deeply nested
            if (isCategory)
            {
                if (ChannelPosition.GetDepth(parent.RawPosition) > 2)
                {
                    return TaskResult<byte>.FromFailure("Categories cannot be nested more than 2 levels deep.");
                }
            }
        }
        
        // Get the highest local position in the category
        var max = await _db.Channels.Where(x => x.PlanetId == planetId && x.ParentId == parentId)
            .MaxAsync(x => x.LocalPosition);

        if (max == byte.MaxValue)
        {
            return TaskResult<byte>.FromFailure("Max position reached");
        }

        // Give the next position
        return TaskResult<byte>.FromData((byte)(max + 1));
    }
    
    public async Task<List<Channel>> GetDescendants(Channel channel)
    {
        if (channel.ChannelType != ChannelTypeEnum.PlanetCategory)
            return [];
        
        var descendants = new List<Channel>();
        await AddDescendants(channel, descendants);

        return descendants;
    }

    public async Task AddDescendants(Channel channel, List<Channel> list)
    {
        var children = await _db.Channels.DirectChildrenOf(channel)
            .Select(x => x.ToModel())
            .ToListAsync();
        
        list.AddRange(children);
        
        foreach (var child in children)
        {
            await AddDescendants(child, list);
        }
    }

    public async Task<TaskResult> MoveChannel(
        Channel toMove, // The channel to be moved
        Channel? destinationCategory, // The category the channel will be moved into
        Channel? destinationChannel, // The channel in the position the channel will be moved to
        bool insertBefore) // If the channel should be inserted before the destination channel, or default after
    {
        if (toMove.PlanetId is null)
            return TaskResult.FromFailure("Non-planet channels cannot be moved.");
        
        var append = false;
        
        // There's a few cases. The easiest is appending to the end.
        // This occurs if the destination channel is the category, or if there is no destination.
        
        if (destinationCategory is not null)
        {
            if (destinationCategory.ChannelType != ChannelTypeEnum.PlanetCategory) 
                return TaskResult.FromFailure("Destination category is not a category.");
            
            if (destinationCategory.Id == toMove.Id)
                return TaskResult.FromFailure("Cannot move a channel into itself.");
            
            // Protect from dangerous channel loops. Although, the way channel lists are built now, this
            // isn't quite as bad as it used to be, since it's not recursive.
            var descendents = await GetDescendants(toMove);
            
            if (descendents.Any(x => x.Id == destinationCategory.Id))
            {
                return TaskResult.FromFailure("Move resulted in a loop");
            }

            if (destinationChannel is null)
            {
                append = true;
            }
            else
            {
                append = destinationChannel.Id == destinationCategory.Id;
            }
        }
        else
        {
            append = true;
        }
        
        await using var trans = await _db.Database.BeginTransactionAsync();

        try
        {

            if (append)
            {
                // Get the next position
                var nextPosResult = await TryGetNextChannelLocalPosition(toMove.PlanetId.Value, destinationCategory);
                if (!nextPosResult.Success)
                    return nextPosResult.WithoutData();

                // Get the old position so we can shift any siblings in the original category
                var oldParent = await _db.Channels.FindAsync(toMove.ParentId);
                var oldPos = toMove.LocalPosition;

                // Move this channel to the new position
                toMove.LocalPosition = nextPosResult.Data;

                // Update the channel's parent
                toMove.ParentId = destinationCategory?.Id ?? null;

                // Shift any siblings that come *after* the removed position back by one
                var shiftResult = await ShiftChannels(toMove.PlanetId.Value, oldParent?.Id, oldPos, -1);
                if (!shiftResult.Success)
                    return shiftResult;
            }
            else
            {
                // We need to shift channels forward to make room for the new channel
                // then insert the channel in the new position
                // then shift the channels at the old position back by one

                if (destinationChannel is null)
                {
                    return TaskResult.FromFailure("Destination channel not found.");
                }

                if (destinationChannel.Id == toMove.Id)
                {
                    // No work to do
                    return TaskResult.SuccessResult;
                }
                
                byte newPos = insertBefore ? destinationChannel!.LocalPosition : (byte)(destinationChannel!.LocalPosition + 1);
                
                // Shift forward by one after the new position
                var shiftResult = await ShiftChannels(toMove.PlanetId.Value, destinationCategory?.Id, newPos, 1);
                
                if (!shiftResult.Success)
                    return shiftResult;
                
                var oldParent = await _db.Channels.FindAsync(toMove.ParentId);
                var oldPos = toMove.LocalPosition;
                
                // Move this channel to the new position
                toMove.LocalPosition = newPos;
                toMove.ParentId = destinationCategory?.Id ?? null;
                
                // Shift any siblings that come *after* the removed position back by one
                var shiftResult2 = await ShiftChannels(toMove.PlanetId.Value, oldParent?.Id, oldPos, -1);
                if (!shiftResult2.Success)
                    return shiftResult2;
            }
            
            var destId = destinationCategory?.Id ?? null;

            // Get all the channels that may have changed
            var changed = await _db.Channels.Where(x => 
                x.PlanetId == toMove.PlanetId &&
                (x.ParentId == toMove.ParentId || x.ParentId == destId))
                .ToListAsync();
            
            var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(toMove.PlanetId.Value);

            ChannelsMovedEvent eventData = new();
            eventData.Moves = new ChannelMove [changed.Count];
            
            // Update channels in hosted planet
            foreach (var change in changed)
            {
                var hostedChannel = hostedPlanet.GetChannel(change.Id);
                if (hostedChannel is not null)
                {
                    hostedChannel.LocalPosition = change.LocalPosition;
                    hostedChannel.ParentId = change.ParentId;
                }
            }
            
            await _db.SaveChangesAsync();
            
            await trans.CommitAsync();
        }
        catch (Exception e)
        {
            await trans.RollbackAsync();
            return TaskResult.FromFailure("An unexpected error occured.");
        }

        return TaskResult.SuccessResult;
    }
    
    
    private static int _migratedChannels = 0;

    public async Task MigrateChannels()
    {
        // From V0 -> V3, we convert the position to the new format
        
        // Non-planet channels just get updated to version 2
        await _db.Channels.Where(x => x.PlanetId == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Version, 8));
        
        var rootChannels = await _db.Channels
            .Where(x => x.Version < 8
                && x.PlanetId != null
                && x.ParentId == null)
            .ToListAsync();
        
        // Build set of planets
        var planets = new HashSet<long>();
        foreach (var root in rootChannels)
        {
            planets.Add(root.PlanetId!.Value);
        }

        foreach (var planetId in planets)
        {
            var rootChannelsForPlanet = rootChannels.Where(x => x.PlanetId == planetId).ToList();
            
            // Sort the root channels by position
            rootChannelsForPlanet.Sort((a, b) => a.LocalPosition.CompareTo(b.LocalPosition));

            var ri = 1;
            foreach (var rootChannel in rootChannelsForPlanet)
            {
                var rootPos = ChannelPosition.AppendRelativePosition(0, (uint)ri, 1);
                ri++;
                
                await MigrateChannel(rootChannel, rootPos);
                
                await _db.SaveChangesAsync();
                
                _logger.LogInformation("Migrated channel tree, total {ChannelCount}", _migratedChannels);
            }
        }
    }

    public async Task MigrateChannel(Valour.Database.Channel channel, uint newPosition)
    {
        channel.RawPosition = newPosition;
        channel.Version = 8;
        
        _db.Channels.Update(channel);
        
        _migratedChannels++;
        
        // Migrate children
        var children = await _db.Channels
            .Where(x => x.ParentId == channel.Id)
            .OrderBy(x => x.LocalPosition)
            .ToListAsync();
        
        var ci = 1;
        foreach (var child in children)
        {
            var childPos = ChannelPosition.AppendRelativePosition(newPosition, (uint)ci);
            ci++;
            await MigrateChannel(child, childPos);
        }
    }
    
    public async Task GetPlanetChannelMembers(long planetId, long channelId)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(planetId);
    }
}