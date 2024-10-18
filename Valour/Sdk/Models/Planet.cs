using Valour.Sdk.Client;
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
    public SortedReactiveModelStore<Channel, long> Channels { get; private set; }
    
    /// <summary>
    /// The chat channels in this planet
    /// </summary>
    public SortedReactiveModelStore<Channel, long> ChatChannels { get; private set; }
    
    /// <summary>
    /// The voice channels in this planet
    /// </summary>
    public SortedReactiveModelStore<Channel, long> VoiceChannels { get; private set; }
    
    /// <summary>
    /// The categories in this planet
    /// </summary>
    public SortedReactiveModelStore<Channel, long> Categories { get; private set; }

    /// <summary>
    /// The primary (default) chat channel of the planet
    /// </summary>
    public Channel PrimaryChatChannel { get; set; }
    
    private List<PlanetRole> Roles { get; set; }
    private List<PlanetMember> Members { get; set; }
    private List<PlanetInvite> Invites { get; set; }
    private List<PermissionsNode> PermissionsNodes { get; set; }

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
        var channel = eventData.Model;
        
        if (Channels is null || channel.PlanetId != Id)
            return;
        
        InsertChannelIntoLists(channel);
    }
    
    
    public Task NotifyRoleUpdateAsync(ModelUpdateEvent<PlanetRole> eventData)
    {
        var role = eventData.Model;
        
        if (Roles is null || role.PlanetId != Id)
            return Task.CompletedTask;

        if (!Roles.Any(x => x.Id == role.Id))
        {
            Roles.Add(role);
            
            Roles.Sort((a, b) => a.Position.CompareTo(b.Position));
        }

        return Task.CompletedTask;
    }
    
    public Task NotifyRoleDeleteAsync(PlanetRole role)
    {
        if (Roles is null || !Roles.Contains(role))
            return Task.CompletedTask;

        Roles.Remove(role);

        return Task.CompletedTask;
    }

    public Task NotifyMemberUpdateAsync(ModelUpdateEvent<PlanetMember> eventData)
    {
        var member = eventData.Model;
        
        if (Members is null || member.PlanetId != Id)
            return Task.CompletedTask;

        if (!Members.Any(x => x.Id == member.Id))
            Members.Add(member);
        
        return Task.CompletedTask;
    }
    
    public Task NotifyMemberDeleteAsync(PlanetMember member)
    {
        if (Members is null || !Members.Contains(member))
            return Task.CompletedTask;

        Members.Remove(member);

        return Task.CompletedTask;
    }
    
    #endregion
    
    /// <summary>
    /// Retrieves and returns a client planet by requesting from the server
    /// </summary>
    public static async ValueTask<Planet> FindAsync(long id, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = Cache.Get(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(id);
        var item = (await node.GetJsonAsync<Planet>($"api/planets/{id}")).Data;

        if (item is not null)
            return await item.SyncAsync();

        return null;
    }
    
    public override Planet AddToCacheOrReturnExisting()
    {
        NodeManager.PlanetToNode[Id] = NodeName;
        return base.AddToCacheOrReturnExisting();
    }

    public async ValueTask<PlanetRole> GetDefaultRoleAsync(bool refresh = false)
    {
        if (Roles == null || refresh)
            await LoadRolesAsync();

        return Roles?.FirstOrDefault(x => x.IsDefault);
    }
    
    private void ClearOrInitChannels()
    {
        Channels = Channels.ClearOrInit();
        ChatChannels = ChatChannels.ClearOrInit();
        VoiceChannels = VoiceChannels.ClearOrInit();
        Categories = Categories.ClearOrInit();
    }

    public void SortChannels()
    {
        Channels.Sort();
        ChatChannels.Sort();
        VoiceChannels.Sort();
        Categories.Sort();
    }

    /// <summary>
    /// Inserts a channel into the planet's channel lists.
    /// If sort is true, the lists will be sorted after insertion.
    /// </summary>
    private void InsertChannelIntoLists(Channel channel, bool sort = true)
    {
        // We already have this channel inserted
        if (Channels.Contains(channel))
            return;

        if (sort)
            Channels.Upsert(channel);
        else
            Channels.UpsertNoSort(channel);
        
        // Note: We don't need to check if the channel is already in these lists
        // because channels are always added to the main list. If it's not there,
        // it's not in any of the other lists.
        
        switch (channel.ChannelType)
        {
            case ChannelTypeEnum.PlanetChat:
            {
                if (sort)
                    ChatChannels.Upsert(channel);
                else
                    ChatChannels.UpsertNoSort(channel);
                
                if (channel.IsDefault)
                    PrimaryChatChannel = channel;
                
                break;
            }
            case ChannelTypeEnum.PlanetCategory:
            {
                if (sort)
                    Categories.Upsert(channel);
                else
                    Categories.UpsertNoSort(channel);
                
                break;
            }
            case ChannelTypeEnum.PlanetVoice:
            {
                if (sort)
                    VoiceChannels.Upsert(channel);
                else
                    VoiceChannels.UpsertNoSort(channel);
                
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
        ClearOrInitChannels();

        foreach (var channel in channels)
        {
            // Sort is false because we will sort at the end
            InsertChannelIntoLists(channel, false);
        }
        
        SortChannels();
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
    /// Returns the members of the planet
    /// </summary>
    public async ValueTask<List<PlanetMember>> GetMembersAsync(bool force_refresh = false)
    {
        if (Members is null || force_refresh)
        {
            await LoadMemberDataAsync();
        }

        return Members;
    }

    /// <summary>
    /// Loads the member data for the planet (this is quite heavy) 
    /// </summary>
    public async Task LoadMemberDataAsync()
    {
        Console.WriteLine("Loading members");

        if (Members is null)
            Members = new List<PlanetMember>();
        else
            Members.Clear();

        var totalCount = 1;

        PlanetMemberInfo currentResult;
        List<PlanetMemberData> allResults = new();

        var page = 0;

        while (page == 0 || page * 100 < totalCount)
        {
            currentResult = (await Node.GetJsonAsync<PlanetMemberInfo>($"{IdRoute}/memberinfo?page={page}")).Data;
            totalCount = currentResult.TotalCount;
            allResults.AddRange(currentResult.Members);

            page++;
        }

        foreach (var info in allResults)
        {
            // Set role id data manually
            await info.Member.SetLocalRoleIds(info.RoleIds);

            // Set in cache
            // Skip event for bulk loading
            var cachedMember = await info.Member.SyncAsync(true);
            await info.User.SyncAsync(true);
            
            Members.Add(cachedMember);
        }
    }

    public async ValueTask<List<PermissionsNode>> GetPermissionsNodesAsync(bool refresh = false)
    {
        if (PermissionsNodes is null || refresh)
            await LoadPermissionsNodesAsync();

        return PermissionsNodes;
    }

    public async Task LoadPermissionsNodesAsync()
    {
        PermissionsNodes =  await PermissionsNode.GetAllForPlanetAsync(Id);
    }

    /// <summary>
    /// Returns the invites of the planet
    /// </summary>
    public async ValueTask<List<PlanetInvite>> GetInvitesAsync(bool refresh = false)
    {
        if (Invites is null || refresh)
            await LoadInvitesAsync();

        return Invites;
    }

    /// <summary>
    /// Loads the planet's invites from the server
    /// </summary>
    public async Task LoadInvitesAsync()
    {
        var invites = (await Node.GetJsonAsync<List<PlanetInvite>>($"api/planets/{Id}/invites")).Data;

        if (invites is null)
            return;

        foreach (var invite in invites)
        {
            // Skip event for bulk loading
            await ModelCache<,>.Put(invite.Id, invite, true);
            await ModelCache<,>.Put(invite.Code, invite, true);
        }

        if (Invites is null)
            Invites = new();
        else
            Invites.Clear();

        foreach (var invite in invites)
        {
            var cInvite = await PlanetInvite.FindAsync(invite.Code);

            if (cInvite is not null)
                Invites.Add(cInvite);
        }
    }

    /// <summary>
    /// Returns the roles of a planet
    /// </summary>
    public async ValueTask<List<PlanetRole>> GetRolesAsync(bool force_refresh = false)
    {
        if (Roles is null || force_refresh)
        {
            await LoadRolesAsync();
        }

        return Roles;
    }

    /// <summary>
    /// Loads the roles of a planet from the server
    /// </summary>
    public async Task LoadRolesAsync()
    {
        var roles = (await Node.GetJsonAsync<List<PlanetRole>>($"{IdRoute}/roles")).Data;

        if (roles is null)
            return;

        foreach (var role in roles)
        {
            // Skip event for bulk loading
            await ModelCache<,>.Put(role.Id, role, true);
        }

        if (Roles is null)
            Roles = new List<PlanetRole>();
        else
            Roles.Clear();

        foreach (var role in roles)
        {
            var cRole = await PlanetRole.FindAsync(role.Id, Id);

            if (cRole is not null)
                Roles.Add(cRole);
        }

        Roles.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    
    /// <summary>
    /// Returns the member for the current user in this planet (if it exists)
    /// </summary>
    public ValueTask<PlanetMember> GetSelfMemberAsync(bool forceRefresh = false)
    {
        return GetMemberByUserAsync(ValourClient.Self.Id, forceRefresh);
    }

    /// <summary>
    /// Returns the member for a given user id
    /// </summary>
    public ValueTask<PlanetMember> GetMemberByUserAsync(long userId, bool forceRefresh = false)
    {
        return PlanetMember.FindAsyncByUser(userId, Id, forceRefresh);
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
