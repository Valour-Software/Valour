﻿using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Economy;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */
public class Planet : ClientModel<Planet, long>, ISharedPlanet
{
    public override string BaseRoute =>
        ISharedPlanet.BaseRoute;

    public override Node Node => 
        NodeManager.GetNodeFromName(NodeName); // Planets have node known

    // Cached values

    // A note to future Spike:
    // These can be referred to and will *never* have their reference change.
    // The lists are updated in realtime which means UI watching the lists do not
    // need to get an updated list. Do not second guess this decision. It is correct.
    // - Spike, 10/05/2024

    /// <summary>
    /// The channels in this planet
    /// </summary>
    public SortedReactiveModelStore<Channel, long> Channels { get; } = new();
    
    /// <summary>
    /// The chat channels in this planet
    /// </summary>
    public SortedReactiveModelStore<Channel, long> ChatChannels { get; } = new();
    
    /// <summary>
    /// The voice channels in this planet
    /// </summary>
    public SortedReactiveModelStore<Channel, long> VoiceChannels { get; } = new();
    
    /// <summary>
    /// The categories in this planet
    /// </summary>
    public SortedReactiveModelStore<Channel, long> Categories { get; } = new();

    /// <summary>
    /// The primary (default) chat channel of the planet
    /// </summary>
    public Channel PrimaryChatChannel { get; private set; }
    
    /// <summary>
    /// The roles in this planet
    /// </summary>
    public SortedReactiveModelStore<PlanetRole, long> Roles { get; } = new();
    
    /// <summary>
    /// The default (everyone) role of this planet
    /// </summary>
    public PlanetRole DefaultRole { get; private set; }
    
    /// <summary>
    /// The members of this planet
    /// </summary>
    public ReactiveModelStore<PlanetMember, long> Members { get; } = new();
    
    /// <summary>
    /// The invites of this planet
    /// </summary>
    public ReactiveModelStore<PlanetInvite, string> Invites { get; } = new();
    
    /// <summary>
    /// The permission nodes of this planet
    /// </summary>
    public ReactiveModelStore<PermissionsNode, long> PermissionsNodes { get; } = new();

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

    #region Child Event Handlers
    
    public void NotifyChannelUpdate(ModelUpdateEvent<Channel> eventData)
    { 
        // We have our own method for this because there are
        // many channel lists to maintain
        UpsertChannel(eventData);
    }
    
    public void NotifyChannelDelete(Channel channel)
    {
        Channels?.Remove(channel);
        ChatChannels?.Remove(channel);
        VoiceChannels?.Remove(channel);
        Categories?.Remove(channel);
    }


    public void NotifyRoleUpdate(ModelUpdateEvent<PlanetRole> eventData)
    {
        if (eventData.Model.IsDefault)
            DefaultRole = eventData.Model;
        
        Roles?.Upsert(eventData);
    }
        
    
    public void NotifyRoleDelete(PlanetRole role) =>
        Roles?.Remove(role);

    public void NotifyMemberUpdate(ModelUpdateEvent<PlanetMember> eventData) =>
        Members?.Upsert(eventData.Model);
    
    public void NotifyMemberDelete(PlanetMember member) =>
        Members?.Remove(member);
    
    #endregion
    
    #region Finding Planet Models
    
    /// <summary>
    /// Returns the member for the given id
    /// </summary>
    public async ValueTask<PlanetMember> FindMemberAsync(long id, bool skipCache = false)
    {
        if (!skipCache && Members.TryGet(id, out var cached))
            return cached;
        
        var member = (await Node.GetJsonAsync<PlanetMember>($"{ISharedPlanetMember.BaseRoute}/{id}")).Data;
        
        return member?.Sync();
    }
    
    /// <summary>
    /// Returns the member for the given id
    /// </summary>
    public async ValueTask<PlanetMember> FindMemberByUserAsync(long userId, bool skipCache = false)
    {
        var key = new PlanetUserKey(userId, Id);
        
        if (!skipCache && PlanetMember.MemberIdLookup.TryGetValue(key, out var id) &&
            Members.TryGet(id, out var cached))
            return cached;
        
        var member = (await Node.GetJsonAsync<PlanetMember>($"{ISharedPlanetMember.BaseRoute}/byuser/{Id}/{userId}", true)).Data;

        return member?.Sync();
    }
    
    /// <summary>
    /// Returns the permissions node for the given values
    /// </summary>
    public async ValueTask<PermissionsNode> FindPermissionsNodeAsync(PermissionsNodeKey key, bool refresh = false)
    {
        if (!refresh && 
            PermissionsNode.PermissionNodeIdLookup.TryGetValue(key, out var id) &&
            PermissionsNodes.TryGet(id, out var cached))
            return cached;
        
        var permNode = (await Node.GetJsonAsync<PermissionsNode>(
                ISharedPermissionsNode.GetIdRoute(key.TargetId, key.RoleId, key.TargetType), 
                true)).Data;
        
        return permNode?.Sync();
    }
    
    #endregion
    
    /// <summary>
    /// Retrieves and returns a client planet by requesting from the server
    /// </summary>
    public static async ValueTask<Planet> FindAsync(long id, bool skipCache = false)
    {
        if (!skipCache && Cache.TryGet(id, out var cached))
            return cached;

        var node = await NodeManager.GetNodeForPlanetAsync(id);
        var planet = (await node.GetJsonAsync<Planet>($"api/planets/{id}")).Data;

        return planet?.Sync();
    }
    
    public override Planet AddToCacheOrReturnExisting()
    {
        NodeManager.PlanetToNode[Id] = NodeName;
        return base.AddToCacheOrReturnExisting();
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
        
        ApplyChannels(newData);
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
            // Set role id data manually
            await info.Member.SetLocalRoleIds(info.RoleIds);

            // Set in cache
            // Skip event for bulk loading
            var cachedMember = info.Member.Sync(true);
            // info.Member.User = info.User; TODO: Always send user with member data
            info.User.Sync(true);
            
            Members.Upsert(cachedMember, true);
        }
        
        Members.NotifySet();
    }

    public async Task FetchPermissionsNodesAsync()
    {
        var permissionsNodes = (await Node.GetJsonAsync<List<PermissionsNode>>(
            ISharedPermissionsNode.GetAllRoute(Id)
        )).Data;
        
        PermissionsNodes.Clear(true);
        
        foreach (var permNode in permissionsNodes)
        {
            // Add or update in cache
            var cached = permNode.Sync();
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
            var cached = invite.Sync(true);
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
            var cached = role.Sync(true);
            
            Roles.UpsertNoSort(cached, true);
        }
        
        Roles.Sort();
        
        Roles.NotifySet();
    }

    
    /// <summary>
    /// Returns the member for the current user in this planet (if it exists)
    /// </summary>
    public ValueTask<PlanetMember> GetSelfMemberAsync(bool skipCache = false)
    {
        return FindMemberByUserAsync(ValourClient.Self.Id, skipCache);
    }
    
    public async Task<TaskResult> SetChildOrderAsync(OrderChannelsModel model) =>
        await Node.PostAsync($"{IdRoute}/planetChannels/order", model);

    public async Task<TaskResult> InsertChild(InsertChannelChildModel model) =>
        await Node.PostAsync($"{IdRoute}/planetChannels/insert", model);

    public Task<PagedResponse<EcoAccount>> GetPlanetAccounts(int skip = 0, int take = 50) =>
        EcoAccount.GetPlanetPlanetAccountsAsync(Id);
    
    public string GetIconUrl(IconFormat format = IconFormat.Webp256) =>
        ISharedPlanet.GetIconUrl(this, format);
}
