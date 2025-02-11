using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Economy;
using Valour.Sdk.Nodes;
using Valour.Sdk.Requests;
using Valour.Sdk.Services;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */
public class Planet : ClientModel<Planet, long>, ISharedPlanet, IDisposable
{
    public override string BaseRoute =>
        ISharedPlanet.BaseRoute;

    private Node _node;
    
    // Cached values

    // A note to future Spike:
    // These can be referred to and will *never* have their reference change.
    // The lists are updated in realtime which means UI watching the lists do not
    // need to get an updated list. Do not second guess this decision. It is correct.
    // - Spike, 10/05/2024

    public HybridEvent<RoleMembershipEvent> RoleMembershipChanged;

    /// <summary>
    /// The channels in this planet
    /// </summary>
    public SortedModelList<Channel, long> Channels { get; } = new();

    /// <summary>
    /// The chat channels in this planet
    /// </summary>
    public SortedModelList<Channel, long> ChatChannels { get; } = new();

    /// <summary>
    /// The voice channels in this planet
    /// </summary>
    public SortedModelList<Channel, long> VoiceChannels { get; } = new();

    /// <summary>
    /// The categories in this planet
    /// </summary>
    public SortedModelList<Channel, long> Categories { get; } = new();

    /// <summary>
    /// The primary (default) chat channel of the planet
    /// </summary>
    public Channel PrimaryChatChannel { get; private set; }

    /// <summary>
    /// The roles in this planet
    /// </summary>
    public SortedModelList<PlanetRole, long> Roles { get; } = new();

    /// <summary>
    /// The default (everyone) role of this planet
    /// </summary>
    public PlanetRole DefaultRole { get; private set; }

    /// <summary>
    /// The members of this planet
    /// </summary>
    public ModelList<PlanetMember, long> Members { get; } = new();

    /// <summary>
    /// The invites of this planet
    /// </summary>
    public ModelList<PlanetInvite, string> Invites { get; } = new();

    /// <summary>
    /// The permission nodes of this planet
    /// </summary>
    public ModelList<PermissionsNode, long> PermissionsNodes { get; } = new();

    /// <summary>
    /// The member for the current user in this planet. Can be null if not a member.
    /// </summary>
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

    internal void SetMyMember(PlanetMember member)
    {
        MyMember = member;
    }

    #region Child Event Handlers

    public void OnChannelUpdated(ModelUpdateEvent<Channel> eventData)
    {
        // We have our own method for this because there are
        // many channel lists to maintain
        UpsertChannel(eventData);
    }

    public void OnChannelDeleted(Channel channel)
    {
        Channels?.Remove(channel);
        ChatChannels?.Remove(channel);
        VoiceChannels?.Remove(channel);
        Categories?.Remove(channel);
    }


    public void OnRoleUpdated(ModelUpdateEvent<PlanetRole> eventData)
    {
        if (eventData.Model.IsDefault)
            DefaultRole = eventData.Model;

        Roles.Upsert(eventData);

        // Let members know
        foreach (var member in Members)
        {
            member.OnRoleUpdated(eventData);
        }
    }

    public void OnRoleDeleted(PlanetRole role)
    {
        Roles.Remove(role);

        // Let members know
        foreach (var member in Members)
        {
            member.OnRoleDeleted(role);
        }
    }

    public void OnMemberUpdated(ModelUpdateEvent<PlanetMember> eventData) =>
        Members.Upsert(eventData.Model);

    public void OnMemberDeleted(PlanetMember member) =>
        Members.Remove(member);

    public void OnMemberRoleAdded(PlanetRoleMember roleMember)
    {
        if (!Members.TryGet(roleMember.MemberId, out var member))
            return;

        if (!Roles.TryGet(roleMember.RoleId, out var role))
            return;

        member.OnRoleAdded(role);

        var eventArgs = new RoleMembershipEvent(MemberRoleEventType.Added, role, member);
        RoleMembershipChanged?.Invoke(eventArgs);
    }

    public void OnMemberRoleRemoved(PlanetRoleMember roleMember)
    {
        if (!Members.TryGet(roleMember.MemberId, out var member))
            return;

        if (!Roles.TryGet(roleMember.RoleId, out var role))
            return;

        member.OnRoleRemoved(role);

        var eventArgs = new RoleMembershipEvent(MemberRoleEventType.Removed, role, member);
        RoleMembershipChanged?.Invoke(eventArgs);
    }

    #endregion

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
    public PagedModelReader<EcoAccount> GetSharedEcoAccountsReader(int pageSize = 50) =>
        Client.EcoService.GetSharedAccountPagedReader(this, pageSize);

    /// <summary>
    /// Used to create channels. Allows specifying permissions nodes.
    /// </summary>
    public Task<TaskResult<Channel>> CreateChannelWithDetails(CreateChannelRequest request) =>
        Client.ChannelService.CreatePlanetChannelAsync(this, request);

    #endregion

    public void SetNode(Node node)
    {
        _node = node;
    }


    protected override void OnDeleted()
    {

    }

    public void Dispose()
    {
        Channels.Dispose();
        ChatChannels.Dispose();
        VoiceChannels.Dispose();
        Categories.Dispose();
        Roles.Dispose();
        Members.Dispose();
        Invites.Dispose();
        PermissionsNodes.Dispose();
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

    public override Planet AddToCacheOrReturnExisting()
    {
        Client.NodeService.SetKnownByPlanet(Id, NodeName);
        return Client.Cache.Planets.Put(Id, this);
    }

    public override Planet TakeAndRemoveFromCache()
    {
        Client.Cache.Planets.Remove(Id);
        return this;
    }

    private void ClearChannels(bool skipEvent = false)
    {
        Channels.Clear(skipEvent);
        ChatChannels.Clear(skipEvent);
        VoiceChannels.Clear(skipEvent);
        Categories.Clear(skipEvent);
    }

    public void SortChannels()
    {
        Channels.Sort();
        ChatChannels.Sort();
        VoiceChannels.Sort();
        Categories.Sort();
    }

    public void NotifyChannelsSet()
    {
        Channels.NotifySet();
        ChatChannels.NotifySet();
        VoiceChannels.NotifySet();
        Categories.NotifySet();
    }

    /// <summary>
    /// Inserts a channel into the planet's channel lists.
    /// If sort is true, the lists will be sorted after insertion.
    /// </summary>
    private void FastUpsertChannel(Channel channel)
    {
        Channels.UpsertNoSort(channel, true);

        switch (channel.ChannelType)
        {
            case ChannelTypeEnum.PlanetChat:
            {
                ChatChannels.UpsertNoSort(channel, true);

                if (channel.IsDefault)
                    PrimaryChatChannel = channel;

                break;
            }
            case ChannelTypeEnum.PlanetCategory:
            {
                Categories.UpsertNoSort(channel, true);

                break;
            }
            case ChannelTypeEnum.PlanetVoice:
            {
                VoiceChannels.UpsertNoSort(channel, true);

                break;
            }
            default:
                Console.WriteLine("[!!!] Planet returned unknown or non-planet channel type!");
                break;
        }
    }

    /// <summary>
    /// Version of UpsertChannel to be used directly with events
    /// </summary>
    /// <param name="eventData"></param>
    private void UpsertChannel(ModelUpdateEvent<Channel> eventData)
    {
        Channels.Upsert(eventData);

        switch (eventData.Model.ChannelType)
        {
            case ChannelTypeEnum.PlanetChat:
            {
                ChatChannels.Upsert(eventData);

                if (eventData.Model.IsDefault)
                    PrimaryChatChannel = eventData.Model;

                break;
            }
            case ChannelTypeEnum.PlanetCategory:
            {
                Categories.Upsert(eventData);
                break;
            }
            case ChannelTypeEnum.PlanetVoice:
            {
                VoiceChannels.Upsert(eventData);
                break;
            }
            default:
                Console.WriteLine("[!!!] Planet returned unknown or non-planet channel type!");
                break;
        }

    }

    /// <summary>
    /// Applies the given channels to the planet, inserting and sorting
    /// them where necessary. Clears any existing channels. Only use this
    /// if you know what you're doing.
    /// </summary>
    public void ApplyChannels(List<Channel> channels)
    {
        ClearChannels(true);

        foreach (var channel in channels)
        {
            // Use fast upsert (no sort) because we will sort at the end
            FastUpsertChannel(channel);
        }

        SortChannels();

        NotifyChannelsSet();
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

        newData.SyncAll(Client.Cache);

        ApplyChannels(newData);
    }
    
    public async Task<Channel> FetchPrimaryChatChannelAsync()
    {
        var channel = (await Node.GetJsonAsync<Channel>($"{IdRoute}/channels/primary")).Data;
        if (channel is null)
            return null;
        
        channel = Client.Cache.Sync(channel);

        PrimaryChatChannel = channel;
        
        Channels.Upsert(channel);
        ChatChannels.Upsert(channel);
        
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
            var cachedMember = Client.Cache.Sync(info.Member, true);
            Members.Upsert(cachedMember, true);
            
            // Set role id data manually
            await cachedMember.SetLocalRoleIds(info.RoleIds);
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

        foreach (var permNode in permissionsNodes)
        {
            // Add or update in cache
            var cached = Client.Cache.Sync(permNode, true);
            PermissionsNodes.Upsert(cached, true);
        }

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

        foreach (var invite in invites)
        {
            var cached = Client.Cache.Sync(invite, true);
            Invites.Upsert(cached, true);
        }

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

        foreach (var role in roles)
        {
            // Skip event for bulk loading
            var cached = Client.Cache.Sync(role, true);
            Roles.UpsertNoSort(cached, true);
        }

        Roles.Sort();

        Roles.NotifySet();
    }

    public void NotifyRoleOrderChange(RoleOrderEvent e)
    {
        for (int i = 0; i < e.Order.Count; i++)
        {
            if (Roles.TryGet(e.Order[i], out var role))
            {
                role.Position = (uint)i;
            }
        }
        
        Roles.Sort();
        
        // Resort members
        foreach (var member in Members)
        {
            member.Roles.Sort();
        }
    }

    public Task<TaskResult> AddMemberRoleAsync(long memberId, long roleId) =>
        Client.PlanetService.AddMemberRoleAsync(memberId, roleId, Id);
    
    public Task<TaskResult> RemoveMemberRoleAsync(long memberId, long roleId) =>
        Client.PlanetService.RemoveMemberRoleAsync(memberId, roleId, Id);
    
    public async Task<TaskResult> SetChildOrderAsync(OrderChannelsModel model) =>
        await Node.PostAsync($"{IdRoute}/channels/order", model);

    public async Task<TaskResult> InsertChild(InsertChannelChildModel model) =>
        await Node.PostAsync($"{IdRoute}/channels/insert", model);

    public ModelQueryEngine<EcoAccount> GetSharedAccountQueryEngine() =>
        Client.EcoService.GetSharedAccountQueryEngine(this);
    
    public ModelQueryEngine<EcoAccountPlanetMember> GetUserAccountQueryEngine() =>
        Client.EcoService.GetUserAccountQueryEngine(this);
    
    public string GetIconUrl(IconFormat format = IconFormat.Webp256) =>
        ISharedPlanet.GetIconUrl(this, format);
}
