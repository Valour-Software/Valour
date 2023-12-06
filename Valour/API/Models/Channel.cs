using Valour.Api.Client;
using Valour.Api.Nodes;
using Valour.Api.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Api.Models;

public class Channel : LiveModel, IChannel, ISharedChannel, IPlanetModel
{
    // Cached values
    // Will only be used for planet channels
    private List<PermissionsNode> PermissionsNodes { get; set; }
    private List<User> MemberUsers { get; set; }
    
    public override string BaseRoute =>
        $"api/channels";
    
    /////////////////////////////////
    // Shared between all channels //
    /////////////////////////////////
    
    public List<ChannelMember> Members { get; set; }
    
    /// <summary>
    /// The name of the channel
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The description of the channel
    /// </summary>
    public string Description { get; set; }
    
    /// <summary>
    /// The type of this channel
    /// </summary>
    public ChannelTypeEnum ChannelType { get; set; }
    
    /// <summary>
    /// The last time a message was sent (or event occured) in this channel
    /// </summary>
    public DateTime LastUpdateTime { get; set; }
    
    /////////////////////////////
    // Only on planet channels //
    /////////////////////////////
    
    /// <summary>
    /// The id of the planet this channel belongs to, if any
    /// </summary>
    public long? PlanetId { get; set; }

    /// <summary>
    /// This is used to allow the IPlanetModel interface to be used
    /// Please ensure you know what you're doing if you use this
    /// </summary>
    long IPlanetModel.PlanetId
    {
        get
        {
            if (PlanetId is null)
            {
                Console.WriteLine("[!!!] Unexpected null PlanetId! This should not happen!");
                return 0;
            }

            return PlanetId.Value;
        }
        set => PlanetId = value;
    }

    /// <summary>
    /// The id of the parent of the channel, if any
    /// </summary>
    public long? ParentId { get; set; }
    
    /// <summary>
    /// The position of the channel in the channel list
    /// </summary>
    public int? Position { get; set; }
    
    /// <summary>
    /// If this channel inherits permissions from its parent
    /// </summary>
    public bool? InheritsPerms { get; set; }
    
    /// <summary>
    /// If this channel is the default channel
    /// </summary>
    public bool? IsDefault { get; set; }

    /// <summary>
    /// Returns the channel for the given id. Requires planetId for
    /// planet channels. Makes a request to the server if the channel
    /// is not cached or refresh is true.
    /// </summary>
    public static async ValueTask<Channel> FindAsync(long id, long? planetId = null, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<Channel>(id);
            if (cached is not null)
                return cached;
        }
        
        Node node;
        if (planetId is null)
        {
            node = ValourClient.PrimaryNode;
        }
        else
        {
            node = await NodeManager.GetNodeForPlanetAsync(id);
        }
        
        var item = (await node.GetJsonAsync<Channel>($"api/channels/{id}", refresh)).Data;

        if (item is not null)
            await item.AddToCache(item);

        return item;
    }

    /// <summary>
    /// Used to create channels. Allows specifying permissions nodes.
    /// </summary>
    public static async Task<TaskResult<Channel>> CreateWithDetails(CreateChannelRequest request)
    {
        Node node;
        if (request.Channel.PlanetId is not null)
        {
            node = await NodeManager.GetNodeForPlanetAsync(request.Channel.PlanetId.Value);
        }
        else
        {
            node = ValourClient.PrimaryNode;
        }
        
        return await node.PostAsyncWithResponse<Channel>($"{request.Channel.BaseRoute}/detailed", request);
    }
    
    /// <summary>
    /// Used to speed up direct channel lookups
    /// </summary>
    public static readonly Dictionary<(long, long), long> DirectChannelIdLookup = new();

    /// <summary>
    /// Given a user id, returns the direct channel between them and the requester.
    /// If create is true, this will create the channel if it is not found.
    /// </summary>
    public static async ValueTask<Channel> GetDirectChannelAsync(long otherUserId, bool create = false, bool refresh = false)
    {
        // We insert into the cache with lower-value id first to ensure a match
        // so we do the same to get it back
        var lowerId = ValourClient.Self.Id;
        var higherId = otherUserId;

        if (lowerId > higherId)
        {
            // Swap
            (lowerId, higherId) = (higherId, lowerId);
        }

        var key = (lowerId, higherId);

        if (DirectChannelIdLookup.TryGetValue(key, out var id))
        {
            var cached = ValourCache.Get<Channel>(id);
            if (cached is not null)
                return cached;
        }
        
        var item = (await ValourClient.PrimaryNode.GetJsonAsync<Channel>($"api/channels/direct/{otherUserId}?create={create}")).Data;

        if (item is not null)
        {
            DirectChannelIdLookup.Add(key, item.Id);
            await item.AddToCache(item);
        }

        return item;
    }
    
    /// <summary>
    /// Returns the planet for this channel, if any
    /// </summary>
    public ValueTask<Planet> GetPlanetAsync(bool refresh = false)
    {
        if (PlanetId is null)
            return ValueTask.FromResult<Planet>(null);
        
        return Planet.FindAsync(PlanetId.Value, refresh);
    }

    /// <summary>
    /// Returns the parent of this channel, if any
    /// </summary>
    public ValueTask<Channel> GetParentAsync(bool refresh = false)
    {
        if (ParentId is null)
            return ValueTask.FromResult<Channel>(null);

        return FindAsync(ParentId.Value, PlanetId, refresh);
    }

    /// <summary>
    /// Returns if the channel is unread
    /// </summary>
    public bool DetermineUnread()
    {
        if (!ISharedChannel. ChatChannelTypes.Contains(ChannelType))
            return false;

        return ValourClient.GetChannelUnreadState(Id);
    }

    /// <summary>
    /// Sends a ping to the server that the user is typing
    /// </summary>
    public async Task SendIsTyping()
    {
        if (!ISharedChannel.ChatChannelTypes.Contains(ChannelType))
            return;
        
        await Node.PostAsync($"{IdRoute}/typing", null);
    }
    
    /// <summary>
    /// Returns the permission node for the given role
    /// Channel type allows getting the node for a specific type of channel in a category,
    /// for normal channels this chan be ignored
    /// </summary>
    public async Task<PermissionsNode> GetPermNodeAsync(long roleId, ChannelTypeEnum? type = null, bool refresh = false)
    {
        if (type is null)
            type = ChannelType;
        
        if (PermissionsNodes is null || refresh)
            await LoadPermissionNodesAsync(refresh);
        
        return PermissionsNodes!.FirstOrDefault(x => x.TargetId == roleId && x.TargetType == type);
    }
    
    /// <summary>
    /// Requests and caches nodes from the server
    /// </summary>
    private async Task LoadPermissionNodesAsync(bool refresh = false)
    {
        var planet = await GetPlanetAsync();
        var allPermissions = await planet.GetPermissionsNodesAsync(refresh);
        
        if (PermissionsNodes is not null)
            PermissionsNodes.Clear();
        else
            PermissionsNodes = new List<PermissionsNode>();
        
        foreach (var node in allPermissions)
        {
            if (node.TargetId == Id)
                PermissionsNodes.Add(node);
        }
    }

    /// <summary>
    /// Quick lookup for the channel type names
    /// </summary>
    private static readonly string[] ChannelTypeNames = new[]
    {
        "Planet Chat",
        "Planet Category",
        "Planet Voice",
        "Direct Chat",
        "Direct Voice",
        "Group Chat",
        "Group Voice"
    };
    
    /// <summary>
    /// Returns a good name string for the channel type
    /// </summary>
    public string GetHumanReadableName()
    {
        var index = (int)ChannelType;
        if (index < 0 || index > ChannelTypeNames.Length - 1)
            return "Unknown";

        return ChannelTypeNames[index];
    }

    public async Task<bool> HasPermissionAsync(long userId, Permission permission)
    {
        if (PlanetId is null)
            return true;
        
        var member = await PlanetMember.FindAsyncByUser(userId, PlanetId.Value);
        return await HasPermissionAsync(member, permission);
    }
    
    public async Task<bool> HasPermissionAsync(PlanetMember member, Permission permission)
    {
        // No permission rules for non-planet channels (at least for now)
        if (PlanetId is null)
            return true;

        // Member is from another planet
        if (member.PlanetId != PlanetId)
            return false;
        
        var planet = await member.GetPlanetAsync();

        // Owners have all permissions
        if (planet.OwnerId == member.UserId)
            return true;
        
        var memberRoles = await member.GetRolesAsync();

        var target = this;

        // Move up until no longer inheriting
        while (target.InheritsPerms is not null &&
               target.InheritsPerms.Value &&
               target.ParentId is not null)
        {
            target = await target.GetParentAsync();
        }

        var viewPerm = PermissionState.Undefined;

        foreach (var role in memberRoles)
        {
            var node = await GetPermNodeAsync(role.Id, permission.TargetType);
            if (node is null)
                continue;

            viewPerm = node.GetPermissionState(ChatChannelPermissions.View, true);
            if (viewPerm != PermissionState.Undefined)
                break;
        }
        
        var topRole = memberRoles.FirstOrDefault() ?? PlanetRole.DefaultRole;

        if (viewPerm == PermissionState.Undefined)
        {
            viewPerm = Permission.HasPermission(topRole.ChatPermissions, ChatChannelPermissions.View) ? PermissionState.True : PermissionState.False;
        }

        if (viewPerm != PermissionState.True)
            return false;

        // Go through roles in order
        foreach (var role in memberRoles)
        {
            var node = await GetPermNodeAsync(role.Id, permission.TargetType);
            if (node is null)
                continue;

            // (A lot of the logic here is identical to the server-side PlanetMemberService.HasPermissionAsync)

            // If there is no view permission, there can't be any other permissions
            // View is always 0x01 for channel permissions, so it is safe to use ChatChannelPermission.View for
            // all cases.

            var state = node.GetPermissionState(permission, true);

            switch (state)
            {
                case PermissionState.Undefined:
                    continue;
                case PermissionState.True:
                    return true;
                case PermissionState.False:
                default:
                    return false;
            }
        }

        // Fallback to base permissions
        switch (permission)
        {
            case ChatChannelPermission:
                return Permission.HasPermission(topRole.ChatPermissions, permission);
            case CategoryPermission:
                return Permission.HasPermission(topRole.CategoryPermissions, permission);
            case VoiceChannelPermission:
                return Permission.HasPermission(topRole.VoicePermissions, permission);
            default:
                throw new Exception("Unexpected permission type: " + permission.GetType().Name);
        }
    }
    
    /// <summary>
    /// Returns the current total permissions for this channel for a member.
    /// This result is NOT SYNCED, since it flattens several nodes into one!
    /// </summary>
    public async ValueTask<PermissionsNode> GetFlattenedPermissionsAsync(long memberId, bool forceRefresh = false)
    {
        if (PlanetId is null)
            return null;
        
        var member = await PlanetMember.FindAsync(memberId, PlanetId.Value);
        var roles = await member.GetRolesAsync();

        // Start with no permissions
        var dummyNode = new PermissionsNode()
        {
            // Full, since values should either be yes or no
            Mask = Permission.FullControl,
            // Default to no permission
            Code = 0x0,

            PlanetId = PlanetId.Value,
            TargetId = Id,
            TargetType = ChannelType
        };

        var planet = await GetPlanetAsync();

        // Easy cheat for owner
        if (planet.OwnerId == member.UserId)
        {
            dummyNode.Code = Permission.FullControl;
            return dummyNode;
        }

        // Should be in order of most power -> least,
        // so we reverse it here
        for (int i = roles.Count - 1; i >= 0; i--)
        {
            var role = roles[i];
            PermissionsNode node;
            // If true, we grab the parent's permission node
            if (InheritsPerms == true)
                node = await (await GetParentAsync()).GetPermNodeAsync(role.Id, ChannelType, forceRefresh);
            else
                node = await GetPermNodeAsync(role.Id, ChannelType, forceRefresh);

            if (node is null)
                continue;

            //Console.WriteLine($"{role.Name}: {node.Mask} {node.Code}");

            foreach (var perm in ChatChannelPermissions.Permissions)
            {
                var val = node.GetPermissionState(perm);

                // Change nothing if undefined. Otherwise overwrite.
                // Since most important nodes come last, we will end with correct perms.
                if (val == PermissionState.True)
                {
                    dummyNode.SetPermission(perm, PermissionState.True);
                }
                else if (val == PermissionState.False)
                {
                    dummyNode.SetPermission(perm, PermissionState.False);
                }
            }
        }

        return dummyNode;
    }

    /// <summary>
    /// Returns the last (count) messages
    /// </summary>
    public Task<List<Message>> GetLastMessagesAsync(int count = 10) =>
        GetMessagesAsync(long.MaxValue, count);
    
    /// <summary>
    /// Returns the last (count) messages starting at (index)
    /// </summary>
    public async Task<List<Message>> GetMessagesAsync(long index = long.MaxValue,
        int count = 10)
    {
        if (!ISharedChannel.ChatChannelTypes.Contains(ChannelType))
            return new List<Message>();
        
        var result = await ValourClient.PrimaryNode.GetJsonAsync<List<Message>>($"{IdRoute}/messages?index={index}&count={count}");
        if (!result.Success)
        {
            Console.WriteLine($"Failed to get messages from {Id}: {result.Message}");
            return new List<Message>();
        }
        
        return result.Data;
    }
    
    /// <summary>
    /// Returns the list of users in the channel but DO NOT use this for
    /// planet channels please we will figure that out soon
    /// </summary>
    public async Task<List<User>> GetChannelMemberUsersAsync(bool refresh = false)
    {
        if (PlanetId is null)
            return new List<User>();

        if (Members is null)
        {
            var result =  await ValourClient.PrimaryNode.GetJsonAsync<List<User>>(IdRoute + "/nonPlanetMembers");
            if (result.Success)
                MemberUsers = result.Data;
            else
                return new List<User>();
        }

        return MemberUsers;
    }

    public async Task Open()
    {
        switch (ChannelType)
        {
            case ChannelTypeEnum.PlanetChat:
                await ValourClient.OpenPlanetChannel(this);
                break;
            default:
                break;
        }
    }

    public async Task Close()
    {
        switch (ChannelType)
        {
            case ChannelTypeEnum.PlanetChat:
                await ValourClient.ClosePlanetChannel(this);
                break;
            default:
                break;
        }
    }
}
