using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Sdk.Nodes;
using Valour.Sdk.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Channels;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Models;

/// <summary>
/// The DirectChannelKey uses two user ids to build a key to get the direct channel between them
/// </summary>
public readonly struct DirectChannelKey : IEquatable<DirectChannelKey>
{
    private readonly long _lowerId;
    private readonly long _higherId;

    public DirectChannelKey(long user1Id, long user2Id)
    {
        if (user1Id < user2Id)
        {
            _lowerId = user1Id;
            _higherId = user2Id;
        }
        else
        {
            _lowerId = user2Id;
            _higherId = user1Id;
        }
    }

    public bool Equals(DirectChannelKey other)
    {
        return _lowerId == other._lowerId && _higherId == other._higherId;
    }
}

public class Channel : ClientPlanetModel<Channel, long>, ISharedChannel
{
    public override string BaseRoute => ISharedChannel.GetBaseRoute(this);
    public override string IdRoute => ISharedChannel.GetIdRoute(this);

    /// <summary>
    /// Run when a channel sends a watching update
    /// </summary>
    public HybridEvent<ChannelWatchingUpdate> WatchingUpdated;

    /// <summary>
    /// Run when a channel sends a currently typing update
    /// </summary>
    public HybridEvent<ChannelTypingUpdate> TypingUpdated;
    
    /// <summary>
    /// Run when a message is received in this channel
    /// </summary>
    public HybridEvent<Message> MessageReceived;
    
    /// <summary>
    /// Run when a message is edited in this channel
    /// </summary>
    public HybridEvent<Message> MessageEdited;
    
    /// <summary>
    /// Run when a message is deleted in this channel
    /// </summary>
    public HybridEvent<Message> MessageDeleted;
    
    // Cached values
    // Will only be used for planet channels
    private List<User> _memberUsers;
    
    /// <summary>
    /// Cached parent which should be linked when channels are received
    /// </summary>
    public Channel Parent { get; set; }

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
    /// The planet this channel belongs to, if any
    /// </summary>
    public long? PlanetId { get; set; }
    protected override long? GetPlanetId() => PlanetId;

    /// <summary>
    /// The id of the parent of the channel, if any
    /// </summary>
    public long? ParentId { get; set; }
    
    // Backing store for RawPosition
    private uint _rawPosition;
    
    /// <summary>
    /// The position of the channel. Works as the following:
    /// [8 bits]-[8 bits]-[8 bits]-[8 bits]
    /// Each 8 bits is a category, with the first category being the top level
    /// So for example, if a channel is in the 3rd category of the 2nd category of the 1st category,
    /// [00000011]-[00000010]-[00000001]-[00000000]
    /// This does limit the depth of categories to 4, and the highest position
    /// to 254 (since 000 means no position)
    /// </summary>
    public uint RawPosition
    {
        get => _rawPosition;
        set
        {
            _rawPosition = value;
            Position = new ChannelPosition(value);
        }
    }

    [JsonIgnore]
    public ChannelPosition Position { get; protected set; }

    /// <summary>
    /// If this channel inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }

    /// <summary>
    /// If this channel is the default channel
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Used to limit typing updates
    /// </summary>
    private DateTime _lastTypingUpdateSend = DateTime.UtcNow;

    protected override void OnUpdated(ModelUpdatedEvent<Channel> eventData)
    {
    }
    
    protected override void OnDeleted()
    {
    }
    
    /// <summary>
    /// Opens a connection to realtime planet channel data.
    /// Do not use for non-planet channels!
    /// </summary>
    public async Task<TaskResult> ConnectToRealtime()
    {
        if (PlanetId is null)
            return new TaskResult(false, "Cannot connect to realtime data for non-planet channels. The primary node handles this.");
        
        await Planet.EnsureReadyAsync();
        return await Planet.Node.ConnectToPlanetChannelRealtime(this);
    }
    
    /// <summary>
    /// Disconnects from realtime planet channel data
    /// </summary>
    public async Task<TaskResult> DisconnectFromRealtime()
    {
        if (PlanetId is null)
            return TaskResult.SuccessResult;
        
        // No node = no realtime
        if (Planet.Node is null)
            return TaskResult.SuccessResult;
        
        return await Planet.Node.DisconnectFromChannelRealtime(this);
    }
    
    public override Channel AddToCache(bool skipEvents = false)
    {
        // Add to direct channel lookup if needed
        if (ChannelType == ChannelTypeEnum.DirectChat)
        {
            if (Members is not null && Members.Count == 2)
            {
                var key = new DirectChannelKey(Members[0].UserId, Members[1].UserId);
                Client.Cache.DmChannelKeyToId[key] = Id;
            }
        }
        
        return PlanetId is null ? Client.Cache.Channels.Put(this, skipEvents) : 
                                        Planet.Channels.Put(this, skipEvents);
    }
    
    public override Channel RemoveFromCache()
    {
        // Remove from direct channel lookup if needed
        if (ChannelType == ChannelTypeEnum.DirectChat)
        {
            if (Members is not null && Members.Count == 2)
            {
                var key = new DirectChannelKey(Members[0].UserId, Members[1].UserId);
                Client.Cache.DmChannelKeyToId.Remove(key);
            }
        }
        
        Client.Cache.Channels.Remove(Id);
        
        return this;
    }

    /// <summary>
    /// Returns if this channel is a chat channel
    /// </summary>
    public bool IsChatChannel => ISharedChannel.ChatChannelTypes.Contains(ChannelType);

    /// <summary>
    /// Returns if the channel is unread
    /// </summary>
    public bool DetermineUnread()
    {
        if (!ISharedChannel.ChatChannelTypes.Contains(ChannelType))
            return false;

        return Client.UnreadService.IsChannelUnread(PlanetId, Id);
    }

    /// <summary>
    /// Sends a ping to the server that the user is typing
    /// </summary>
    public async Task SendIsTyping()
    {
        // Limit spam
        if (_lastTypingUpdateSend.Subtract(DateTime.UtcNow).TotalSeconds < 5)
        {
            return;
        }
        
        if (!ISharedChannel.ChatChannelTypes.Contains(ChannelType))
            return;

        if (Node is null) // Need to make strategy for DMs in the future
            return;

        _lastTypingUpdateSend = DateTime.UtcNow;
        
        await Node.PostAsync($"{IdRoute}/typing", null);
    }

    public async Task UpdateUserState(DateTime? updateTime)
    {
        var request = new UpdateUserChannelStateRequest()
        {
            UpdateTime = updateTime ?? DateTime.UtcNow
        };
        
        var result = await Node.PostAsyncWithResponse<UserChannelState>($"{IdRoute}/state", request);

        if (result.Success)
        {
            Client.ChannelStateService.OnUserChannelStateUpdated(result.Data);
        }
        else
        {
            Client.Logger.Log("Channel", "Failed to update user state: " + result.Message, "yellow");
        }
    }

    /// <summary>
    /// Returns the permission node for the given role
    /// Channel type allows getting the node for a specific type of channel in a category,
    /// for normal channels this chan be ignored
    /// </summary>
    public async ValueTask<PermissionsNode> GetPermNodeAsync(long roleId, ChannelTypeEnum? type = null, bool refresh = false)
    {
        if (type is null)
            type = ChannelType;

        PermissionsNodeKey key = new(Id, roleId, type.Value);

        if (Client.Cache.PermNodeKeyToId.TryGetValue(key, out var nodeId))
        {
            if (Planet.PermissionsNodes.TryGet(nodeId, out var cached))
            {
                return cached;
            }
        }

        var node = await Planet.FetchPermissionsNodeAsync(key);
        
        return node;
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
        var index = (int) ChannelType;
        if (index < 0 || index > ChannelTypeNames.Length - 1)
            return "Unknown";

        return ChannelTypeNames[index];
    }

    public async Task<bool> HasPermissionAsync(long userId, Permission permission)
    {
        if (PlanetId is null)
            return true;

        var member = await Planet.FetchMemberByUserAsync(userId);
        return await HasPermissionAsync(member, permission);
    }

    public Channel GetParent()
    {
        if (ParentId is null)
            return null;
        
        return Planet.Channels.TryGet(ParentId.Value, out var parent) ? parent : null;
    }

    public Channel GetPermissionSource()
    {
        var source = this;

        while (source is not null && source.InheritsPerms)
        {
            source = source.GetParent();
        }
        
        return source;
    }

    public async Task<bool> HasPermissionAsync(PlanetMember member, Permission permission)
    {
        // No permission rules for non-planet channels (at least for now)
        if (PlanetId is null)
            return true;
        
        // Member is from another planet
        if (member.PlanetId != PlanetId)
            return false;
        
        // Owners have all permissions
        if (Planet.OwnerId == member.UserId)
            return true;
        
        var viewPerm = PermissionState.Undefined;

        foreach (var role in member.Roles)
        {
            var node = await GetPermNodeAsync(role.Id, permission.TargetType);
            if (node is null)
                continue;

            viewPerm = node.GetPermissionState(ChatChannelPermissions.View, true);
            if (viewPerm != PermissionState.Undefined)
                break;
        }

        var topRole = member.PrimaryRole ?? PlanetRole.DefaultRole;

        if (viewPerm == PermissionState.Undefined)
        {
            viewPerm = Permission.HasPermission(topRole.ChatPermissions, ChatChannelPermissions.View)
                ? PermissionState.True
                : PermissionState.False;
        }

        if (viewPerm != PermissionState.True)
            return false;

        // Go through roles in order
        foreach (var role in member.Roles)
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
        
        var member = await Planet.FetchMemberAsync(memberId);

        // Start with no permissions
        var dummyNode = new PermissionsNode()
        {
            // Full, since values should either be yes or no
            Mask = Permission.FULL_CONTROL,
            // Default to no permission
            Code = 0x0,

            PlanetId = PlanetId.Value,
            TargetId = Id,
            TargetType = ChannelType
        };
        
        // Easy cheat for owner
        if (Planet.OwnerId == member.UserId)
        {
            dummyNode.Code = Permission.FULL_CONTROL;
            return dummyNode;
        }
        
        // Should be in order of most power -> least,
        // so we reverse it here
        for (int i = member.Roles.Count - 1; i >= 0; i--)
        {
            var role = member.Roles[i];
            PermissionsNode node;
            // If true, we grab the parent's permission node
            if (InheritsPerms == true)
                node = await Parent.GetPermNodeAsync(role.Id, ChannelType, forceRefresh);
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

        var result = await Node.GetJsonAsync<List<Message>>(
                $"{IdRoute}/messages?index={index}&count={count}");
        
        if (!result.Success)
        {
            Client.Logger.Log("Channel",$"Failed to get messages from {Id}: {result.Message}", "Yellow");
            return new List<Message>();
        }

        result.Data.SyncAll(Client);

        return result.Data;
    }

    public Task<List<PlanetMember>> FetchRecentChattersAsync() =>
        Client.ChannelService.FetchRecentChattersAsync(this);

    /// <summary>
    /// Returns the list of users in the channel
    /// </summary>
    public async Task<List<User>> GetChannelMemberUsersAsync(bool refresh = false)
    {
        if (PlanetId is null)
            return new List<User>();

        if (Members is null)
        {
            var result = await Node.GetJsonAsync<List<User>>(IdRoute + "/nonPlanetMembers");
            if (result.Success)
                _memberUsers = result.Data;
            else
                return new List<User>();
        }

        return _memberUsers;
    }

    public string GetDescription()
    {
        if (Description is not null)
            return Description;
        return "A " + GetHumanReadableName() + " channel";
    }

    public async Task<string> GetIconAsync()
    {
        var result = "./_content/Valour.Client/media/logo/logo-128.webp";
        
        if (PlanetId is not null)
        {
            result = Planet.GetIconUrl(IconFormat.Webp64);
        }
        else
        {
            var others = Members.Where(x => x.UserId != Client.Me.Id).ToList();
            if (!others.Any())
                result =  Client.Me.GetAvatar();

            var other = await Client.UserService.FetchUserAsync(others.First().UserId);
            if (other is not null)
                result = other.GetAvatar();
        }

        return result;
    }

    public async Task<string> GetTitleAsync()
    {
        if (PlanetId is not null)
            return Name;
        
        var others = Members.Where(x => x.UserId != Client.Me.Id).ToList();

        if (!others.Any())
            return "Chat with yourself";
        
        var sb = new StringBuilder("Chat with ");
        
        var i = 0;
        foreach (var other in others)
        {
            var user = await Client.UserService.FetchUserAsync(other.UserId);
            
            sb.Append(user.Name);
            if (i < others.Count - 1)
                sb.Append(", ");
            else
                sb.Append(' ');
            
            i++;
        }

        return sb.ToString();
    }

    public async Task<TaskResult<Message>> SendMessageAsync(string content, List<MessageAttachment> attachments = null, List<Mention> mentions = null, Embed embed = null)
    {
        if (!IsChatChannel)
            return new TaskResult<Message>(false, "Cannot send messages to non-chat channels.");
        
        var msg = new Message()
        {
            Content = content,
            ChannelId = Id,
            PlanetId = PlanetId,
            AuthorUserId = Client.Me.Id,
            Fingerprint = Guid.NewGuid().ToString(),
        };
        
        if (PlanetId is not null)
        {
            msg.AuthorMemberId = Planet.MyMember.Id;
        }
        
        if (mentions is not null)
            msg.MentionsData = JsonSerializer.Serialize(mentions);
        
        if (attachments is not null)
            msg.AttachmentsData = JsonSerializer.Serialize(attachments);
        
        if (embed is not null)
            msg.EmbedData = JsonSerializer.Serialize(embed);

        return await Client.MessageService.SendMessage(msg);
    }

    public async Task Open(string key)
    {
        switch (ChannelType)
        {
            case ChannelTypeEnum.PlanetChat:
                await Client.ChannelService.TryOpenPlanetChannelConnection(this, key);
                break;
            case ChannelTypeEnum.DirectChat:
                await UpdateUserState(DateTime.UtcNow); // Update the user state
                break;
            default:
                break;
        }
    }

    public async Task Close(string key)
    {
        switch (ChannelType)
        {
            case ChannelTypeEnum.PlanetChat:
                await Client.ChannelService.TryClosePlanetChannelConnection(this, key);
                break;
            default:
                break;
        }
    }

    public void NotifyMessageReceived(Message message)
    {
        if (message.ChannelId != Id)
            return;
        
        MessageReceived?.Invoke(message);
    }
    
    public void NotifyMessageEdited(Message message)
    {
        if (message.ChannelId != Id)
            return;
        
        MessageEdited?.Invoke(message);
    }
    
    public void NotifyMessageDeleted(Message message)
    {
        if (message.ChannelId != Id)
            return;
        
        MessageDeleted?.Invoke(message);
    }

    public int Compare(Channel x, Channel y)
    {
        return x.RawPosition.CompareTo(y.RawPosition);
    }
}
