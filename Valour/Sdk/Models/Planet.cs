using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Economy;
using Valour.Sdk.Nodes;
using Valour.Sdk.Requests;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Queries;

namespace Valour.Sdk.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */
public class Planet : ClientModel<Planet, long>, ISharedPlanet, IDisposable
{
    public override string BaseRoute =>
        ISharedPlanet.BaseRoute;

    private Node _node;

    /// <summary>
    /// In the case that the roles list for some reason has not loaded,
    /// this provides a default role to fall back on
    /// </summary>
    private PlanetRole PlaceholderDefaultRole => new PlanetRole(Client)
    {
        PlanetId = Id,
        Name = PlanetRole.DefaultRole.Name,
        ChatPermissions = PlanetRole.DefaultRole.ChatPermissions,
        VoicePermissions = PlanetRole.DefaultRole.VoicePermissions,
        CategoryPermissions = PlanetRole.DefaultRole.CategoryPermissions,
        FlagBitIndex = PlanetRole.DefaultRole.FlagBitIndex,
        IsDefault = true,
        IsAdmin = false,
    };

    #region Model Caches
    
    ////////////
    // Caches //
    ////////////

    // A note to future Spike:
    // These can be referred to and will *never* have their reference change.
    // The lists are updated in realtime which means UI watching the lists do not
    // need to get an updated list. Do not second guess this decision. It is correct.
    // - Spike, 10/05/2024

    /// <summary>
    /// The channels in this planet
    /// </summary>
    public readonly SortedModelStore<Channel, long> Channels = new();
    
    /// <summary>
    /// The roles in this planet
    /// </summary>
    public readonly SortedModelStore<PlanetRole, long> Roles = new();
    
    /// <summary>
    /// The loaded members of this planet. Will not contain all members.
    /// </summary>
    public readonly ModelStore<PlanetMember, long> Members = new();

    /// <summary>
    /// The loaded invites of this planet
    /// </summary>
    public readonly ModelStore<PlanetInvite, string> Invites = new();

    /// <summary>
    /// The loaded permission nodes of this planet
    /// </summary>
    public readonly ModelStore<PermissionsNode, long> PermissionsNodes = new();
    
    /// <summary>
    /// The loaded bans of this planet
    /// </summary>
    public readonly ModelStore<PlanetBan, long> Bans = new();
    
    /// <summary>
    /// A map from role membership to the contained roles
    /// </summary>
    private readonly ConcurrentDictionary<PlanetRoleMembership, ImmutableList<PlanetRole>> _membershipToRoles = new();
    
    /// <summary>
    /// A map from role flag index to role
    /// </summary>
    private readonly ConcurrentDictionary<int, PlanetRole> _indexToRole = new();
    
    /// <summary>
    /// A map from a channel position to the channel
    /// </summary>
    private readonly ConcurrentDictionary<ChannelPosition, Channel> _positionToChannel = new();
    
    #endregion

    /// <summary>
    /// The primary (default) chat channel of the planet
    /// </summary>
    public Channel PrimaryChatChannel => 
        Channels.FirstOrDefault(x => x.IsDefault) ?? 
        Channels.FirstOrDefault(x => x.ChannelType == ChannelTypeEnum.PlanetChat);

    /// <summary>
    /// The default (everyone) role of this planet
    /// </summary>
    public PlanetRole DefaultRole => Roles.LastOrDefault() ?? PlaceholderDefaultRole;

    /// <summary>
    /// The member for the current user in this planet. Can be null if not a member.
    /// </summary>
    [JsonIgnore]
    [IgnoreRealtimeChanges]
    public PlanetMember MyMember { get; private set; }

    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    public long OwnerId { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The node this planet belongs to
    /// </summary>
    public string NodeName { get; set; }

    /// <summary>
    /// True if the planet has a custom icon
    /// </summary>
    public bool HasCustomIcon { get; set; }

    /// <summary>
    /// True if the planet has an animated icon
    /// </summary>
    public bool HasAnimatedIcon { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    public bool Public { get; set; }

    /// <summary>
    /// If this and public are true, a planet will appear on the discovery tab
    /// </summary>
    public bool Discoverable { get; set; }

    /// <summary>
    /// True if you probably shouldn't be on this server at work owo
    /// </summary>
    public bool Nsfw { get; set; }
    
    /// <summary>
    /// The version of the planet. Used for cache busting.
    /// </summary>
    public int Version { get; set; }
    
    /// <summary>
    /// True if the planet has a custom background
    /// </summary>
    public bool HasCustomBackground { get; set; }
    
    public List<PlanetTag> Tags { get; set; }

    internal void SetMyMember(PlanetMember member)
    {
        MyMember = member;
    }

    #region Planet Sub-Model CRUD

    /// <summary>
    /// Returns the member for the given id
    /// </summary>
    public async ValueTask<PlanetMember> FetchMemberAsync(long id, bool skipCache = false) =>
        await Client.PlanetService.FetchMemberAsync(id, this, skipCache);

    /// <summary>
    /// Returns the member for the given id
    /// </summary>
    public ValueTask<PlanetMember> FetchMemberByUserAsync(long userId, bool skipCache = false) =>
        Client.PlanetService.FetchMemberByUserAsync(userId, this, skipCache);

    /// <summary>
    /// Returns the role for the given id
    /// </summary>
    public ValueTask<PlanetRole> FetchRoleAsync(long id, bool skipCache = false) =>
        Client.PlanetService.FetchRoleAsync(id, this, skipCache);

    /// <summary>
    /// Returns the permissions node for the given key
    /// </summary>
    public ValueTask<PermissionsNode> FetchPermissionsNodeAsync(PermissionsNodeKey key, bool skipCache = false) =>
        Client.PermissionService.FetchPermissionsNodeAsync(key, this, skipCache);

    /// <summary>
    /// Returns the channel for the given id
    /// </summary>
    public ValueTask<Channel> FetchChannelAsync(long channelId, bool skipCache = false) =>
        Client.ChannelService.FetchPlanetChannelAsync(channelId, this, skipCache);
    
    /// <summary>
    /// Returns the eco account for the given id
    /// </summary>
    public ValueTask<EcoAccount> FetchEcoAccountAsync(long id, bool skipCache = false) =>
        Client.EcoService.FetchEcoAccountAsync(id, this, skipCache);

    /// <summary>
    /// Returns the currency for this planet
    /// </summary>
    public ValueTask<Currency> FetchCurrencyAsync() =>
        Client.EcoService.FetchCurrencyByPlanetAsync(this);

    /// <summary>
    /// Returns a reader for the planet's shared eco accounts
    /// </summary>
    public ModelQueryEngine<EcoAccount> GetSharedEcoAccountsReader(int pageSize = 50) =>
        Client.EcoService.GetSharedAccountPagedReader(this, pageSize);

    /// <summary>
    /// Used to create channels. Allows specifying permissions nodes.
    /// </summary>
    public Task<TaskResult<Channel>> CreateChannelWithDetails(CreateChannelRequest request) =>
        Client.ChannelService.CreatePlanetChannelAsync(this, request);

    #endregion

    [JsonConstructor]
    private Planet() : base()
    {
        SetupPlanet(null);
    }
    
    public Planet(ValourClient client) : base(client)
    {
        SetupPlanet(client);
    }

    private void SetupPlanet(ValourClient? client)
    {
        Roles.Changed += OnRolesChanged;
        
        // Add victor dummy member
        Members.Put(new PlanetMember(client)
        {
            Nickname = "Victor",
            Id = long.MaxValue,
            MemberAvatar = "./_content/Valour.Client/media/logo/logo-256.webp"
        });
    }
    

    public void OnChannelsMoved(ChannelsMovedEvent eventData)
    {
        foreach (var move in eventData.Moves)
        {
            var channel = Channels.Get(move.Key);
            if (channel is null)
                continue;
            
            channel.ParentId = move.Value.NewParentId;
            channel.RawPosition = move.Value.NewRawPosition;
        }
        
        // Sort channels
        Channels.Sort();
    }

    private void OnRolesChanged(IModelEvent<PlanetRole> changeEvent)
    {
        // I think there's probably a smarter way to do this, but for the sake
        // of sanity, whenever there's a change to a role, we just nuke the membership
        // cache. It's just easier. It's not intensive to rebuild.
        _membershipToRoles.Clear();
        Client.Logger.Log<Planet>("Role change detected, clearing membership cache", "magenta");

        switch (changeEvent)
        {
            case ModelAddedEvent<PlanetRole> added:
                _indexToRole[added.Model.FlagBitIndex] = added.Model;
                break;
            case ModelRemovedEvent<PlanetRole> removed:
                _indexToRole.TryRemove(removed.Model.FlagBitIndex, out _);
                break;
            case ModelUpdatedEvent<PlanetRole> updated:
                if (updated.Changes.On(x => x.FlagBitIndex, out var oldIndex, out var newIndex))
                {
                    _indexToRole.TryRemove(oldIndex, out _); // Remove old
                    _indexToRole[newIndex] = updated.Model; // Add new
                }
                break;
        }
    }

    public void SetNode(Node node)
    {
        _node = node;
    }


    protected override void OnDeleted()
    {
        Client?.PlanetService.RemoveJoinedPlanet(this);
    }

    public void Dispose()
    {
        Roles.Changed -= OnRolesChanged;
        
        Channels.Dispose();
        Roles.Dispose();
        Members.Dispose();
        Invites.Dispose();
        PermissionsNodes.Dispose();
        Bans.Dispose();
    }

    public async Task EnsureReadyAsync()
    {
        if (_node is null)
        {
            if (NodeName is not null)
                _node = await Client.NodeService.GetByName(NodeName);
            else
                _node = await Client.NodeService.GetNodeForPlanetAsync(Id);
        }

        // Always also get member of client
        if (MyMember is null)
            MyMember = await FetchMemberByUserAsync(Client.Me.Id);
    }

    public Task<TaskResult> FetchInitialDataAsync() =>
        Client.PlanetService.FetchInitialPlanetDataAsync(this);

    public PlanetRole GetRoleByIndex(int index)
    {
        _indexToRole.TryGetValue(index, out var role);
        return role;
    }
    
    public void SetRoleByIndex(int index, PlanetRole role)
    {
        _indexToRole[index] = role;
    }
    
    public ImmutableList<PlanetRole> GetRolesFromMembership(PlanetRoleMembership membership)
    {
        if (_membershipToRoles.TryGetValue(membership, out var roles))
            return roles;
        
        // Get role for each flag
        var roleList = new List<PlanetRole>(membership.GetRoleCount());
        
        foreach (var index in membership.EnumerateRoleIndices())
        {
            if (_indexToRole.TryGetValue(index, out var role))
            {
                roleList.Add(role);
            }
        }
        
        // Ensure sorted by position rather than index!
        roleList.Sort(ISortable.Comparer);
        
        // Convert to immutable list
        var result = roleList.ToImmutableList();
        
        // Add to cache
        _membershipToRoles[membership] = result;
        
        return result;
    }

    /// <summary>
    /// Connects to realtime planet data
    /// </summary>
    public async Task<TaskResult> ConnectToRealtime()
    {
        await EnsureReadyAsync();
        return await _node.ConnectToPlanetRealtime(this);
    }
    
    /// <summary>
    /// Disconnects from realtime planet data
    /// </summary>
    public async Task<TaskResult> DisconnectFromRealtime()
    {
        // No node = no realtime
        if (_node is null)
            return TaskResult.SuccessResult;
        
        return await _node.DisconnectFromPlanetRealtime(this);
    }

    public override Planet AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        Client.NodeService.SetKnownByPlanet(Id, NodeName);
        return Client.Cache.Planets.Put(this, flags);
    }

    public override Planet RemoveFromCache(bool skipEvents = false)
    {
        return Client.Cache.Planets.Remove(this, skipEvents);
    }

    /// <summary>
    /// Requests an updated list of channels from the server.
    /// Generally should not be necessary if using SignalR data feeds.
    /// </summary>
    public async Task FetchChannelsAsync()
    {
        var newData = (await Node.GetJsonAsync<List<Channel>>($"{IdRoute}/channels")).Data;
        if (newData is null)
            return;

        newData.SyncAll(Client, ModelInsertFlags.Batched);
        
        Channels.Sort();
        Channels.NotifySet();
    }
    
    public async Task<Channel> FetchPrimaryChatChannelAsync()
    {
        if (PrimaryChatChannel is not null)
            return PrimaryChatChannel;
        
        var channel = (await Node.GetJsonAsync<Channel>($"{IdRoute}/channels/primary")).Data;
        if (channel is null)
            return null;

        channel.Sync(Client);
        
        return channel;
    }

    /// <summary>
    /// Loads the member data for the planet (this is quite heavy) 
    /// </summary>
    public async Task FetchMemberDataAsync()
    {
        Console.WriteLine("Loading members");

        PlanetMemberInfo currentResult;
        List<PlanetMemberData> allResults = new();

        // First result to get total count
        currentResult = (await Node.GetJsonAsync<PlanetMemberInfo>($"{IdRoute}/memberinfo?page=0")).Data;

        if (currentResult is null)
            return;

        Members.Clear(true);

        var totalCount = currentResult.TotalCount;
        allResults.AddRange(currentResult.Members);

        // If there are more results to get...
        if (totalCount > 100)
        {
            // Calculate number of pages left to get
            var pagesLeft = (int) Math.Ceiling((float) totalCount / 100) - 1;

            // Create tasks to get the rest of the data
            var tasks = new List<Task<TaskResult<PlanetMemberInfo>>>();
            for (var i = 1; i <= pagesLeft; i++)
            {
                tasks.Add(Node.GetJsonAsync<PlanetMemberInfo>($"{IdRoute}/memberinfo?page={i}"));
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            // Add all results to the list
            foreach (var task in tasks)
            {
                var result = task.Result.Data;
                if (result is not null)
                    allResults.AddRange(result.Members);
            }
        }
        
        foreach (var info in allResults)
        {
            // Set in cache
            // Skip event for bulk loading
            info.Member.Sync(Client, ModelInsertFlags.Batched);
        }

        Members.NotifySet();
    }

    public Task<Dictionary<long, int>> FetchRoleMembershipCountsAsync() =>
        Client.PlanetService.FetchRoleMembershipCountsAsync(this);

    public async Task FetchPermissionsNodesAsync()
    {
        var permissionsNodes = (await Node.GetJsonAsync<List<PermissionsNode>>(
            ISharedPermissionsNode.GetAllRoute(Id)
        )).Data;

        PermissionsNodes.Clear(true);

        permissionsNodes.SyncAll(Client, ModelInsertFlags.Batched);
        
        PermissionsNodes.NotifySet();
    }

    /// <summary>
    /// Loads the planet's invites from the server
    /// </summary>
    public async Task LoadInvitesAsync()
    {
        var invites = (await Node.GetJsonAsync<List<PlanetInvite>>($"api/planets/{Id}/invites")).Data;

        if (invites is null)
            return;

        Invites.Clear(true);

        invites.SyncAll(Client, ModelInsertFlags.Batched);

        Invites.NotifySet();
    }

    /// <summary>
    /// Loads the roles of a planet from the server
    /// </summary>
    public async Task LoadRolesAsync()
    {
        var roles = (await Node.GetJsonAsync<List<PlanetRole>>($"{IdRoute}/roles")).Data;

        if (roles is null)
            return;

        Roles.Clear();

        roles.SyncAll(Client, ModelInsertFlags.Batched);

        Roles.Sort();

        Roles.NotifySet();
    }

    public void NotifyRoleOrderChange(RoleOrderEvent e)
    {
        for (int i = 0; i < e.Order.Count; i++)
        {
            if (Roles.TryGet(e.Order[i], out var role))
            {
                role!.Position = (uint)i;
            }
        }
        
        Roles.Sort();
        
        // Resort role lists in key map
        foreach (var pair in _membershipToRoles)
        {
            _membershipToRoles[pair.Key] = pair.Value.OrderBy(r => r.Position).ToImmutableList();
        }
    }
    
    public void SetChannelByPosition(ChannelPosition position, Channel channel)
    {
        _positionToChannel[position] = channel;
    }
    
    public Channel GetChannelByPosition(ChannelPosition position)
    {
        _positionToChannel.TryGetValue(position, out var channel);
        return channel;
    }

    public Task<TaskResult> AddMemberRoleAsync(long memberId, long roleId) =>
        Client.PlanetService.AddMemberRoleAsync(memberId, roleId, Id);
    
    public Task<TaskResult> RemoveMemberRoleAsync(long memberId, long roleId) =>
        Client.PlanetService.RemoveMemberRoleAsync(memberId, roleId, Id);
    
    

    public ModelQueryEngine<EcoAccount> GetSharedAccountQueryEngine() =>
        Client.EcoService.GetSharedAccountQueryEngine(this);
    
    public ModelQueryEngine<EcoAccountPlanetMember> GetUserAccountQueryEngine() =>
        Client.EcoService.GetUserAccountQueryEngine(this);

    public ModelQueryEngine<PlanetMember> GetMemberQueryEngine() =>
        Client.PlanetService.GetMemberQueryEngine(this);

    public ModelQueryEngine<PlanetBan> GetBanQueryEngine() =>
        Client.PlanetService.GetBanQueryEngine(this);
    
    public string GetIconUrl(IconFormat format = IconFormat.Webp256) =>
        ISharedPlanet.GetIconUrl(this, format);

    public PlanetListInfo ToListInfo()
    {
        return PlanetListInfo.FromPlanet(this);
    }
}
