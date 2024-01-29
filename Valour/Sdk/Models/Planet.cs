using System.Text.Json;
using Markdig.Extensions.TaskLists;
using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Api.Models.Economy;
using Valour.Api.Nodes;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Api.Models;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */
public class Planet : LiveModel, ISharedPlanet
{
    public override string BaseRoute =>
            $"api/planets";

    // Cached values

    private PlanetModelObserver<Channel> AllChannels { get; set; }
    private PlanetModelObserver<Channel> ChatChannels { get; set; }
    private PlanetModelObserver<Channel> VoiceChannels { get; set; }
    private PlanetModelObserver<Channel> Categories { get; set; }
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
    /// The image url for the planet 
    /// </summary>
    public string IconUrl { get; set; }

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

    public Planet()
    {
        // Setup self-observing collections
        AllChannels = new(this);
        ChatChannels = new(this);
        VoiceChannels = new(this);
        Categories = new(this);
    }

    #region Child Event Handlers
    
    
    public Task NotifyRoleUpdateAsync(PlanetRole role, ModelUpdateEvent eventData)
    {
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

    public Task NotifyMemberUpdateAsync(PlanetMember member, ModelUpdateEvent eventData)
    {
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

    public override async Task AddToCache<T>(T item, bool skipEvent = false)
    {
        NodeManager.PlanetToNode[Id] = NodeName;
        await ValourCache.Put(this.Id, this, skipEvent);
    }

    /// <summary>
    /// Retrieves and returns a client planet by requesting from the server
    /// </summary>
    public static async ValueTask<Planet> FindAsync(long id, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<Planet>(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(id);
        var item = (await node.GetJsonAsync<Planet>($"api/planets/{id}")).Data;

        if (item is not null)
            await item.AddToCache(item);

        return item;
    }

    /// <summary>
    /// Returns the primary channel of the planet
    /// </summary>
    public async ValueTask<Channel> GetPrimaryChannelAsync(bool refresh = false)
    {
        if (!ChatChannels.Initialized || refresh)
            await LoadChannelsAsync();
        
        return ChatChannels.FirstOrDefault(x => x.IsDefault == true);
    }

    public async ValueTask<PlanetRole> GetDefaultRoleAsync(bool refresh = false)
    {
        if (Roles == null || refresh)
            await LoadRolesAsync();

        return Roles?.FirstOrDefault(x => x.IsDefault);
    }
    
    public async ValueTask<List<Channel>> GetAllChannelsAsync(bool refresh = false)
    {
        if (!AllChannels.Initialized || refresh)
            await LoadChannelsAsync();

        return AllChannels.GetContents();
    }

    /// <summary>
    /// Returns the categories of this planet
    /// </summary>
    public async ValueTask<List<Channel>> GetCategoriesAsync(bool refresh = false)
    {
        if (!Categories.Initialized || refresh)
            await LoadChannelsAsync();

        return Categories.GetContents();
    }

    /// <summary>
    /// Returns the channels of a planet
    /// </summary>
    public async ValueTask<List<Channel>> GetChatChannelsAsync(bool refresh = false)
    {
        if (!ChatChannels.Initialized || refresh)
            await LoadChannelsAsync();

        return ChatChannels.GetContents();
    }

    /// <summary>
    /// Requests and caches channels from the server
    /// </summary>
    public async Task LoadChannelsAsync()
    {
        var channels = (await Node.GetJsonAsync<List<Channel>>($"{IdRoute}/channels")).Data;
        if (channels is null)
            return;
        
        List<Channel> chatChannels = new();
        List<Channel> voiceChannels = new();
        List<Channel> categories = new();

        foreach (var channel in channels)
        {
            switch (channel.ChannelType)
            {
                case ChannelTypeEnum.PlanetChat:
                    chatChannels.Add(channel);
                    break;
                case ChannelTypeEnum.PlanetCategory:
                    categories.Add(channel);
                    break;
                case ChannelTypeEnum.PlanetVoice:
                    voiceChannels.Add(channel);
                    break;
                default:
                    Console.WriteLine("[!!!] Planet returned unknown or non-planet channel type!");
                    break;
            }
        }

        await AllChannels.Initialize(channels);
        await ChatChannels.Initialize(chatChannels);
        await Categories.Initialize(categories);
        await VoiceChannels.Initialize(voiceChannels);
    }

    /// <summary>
    /// Returns the voice channels of a planet
    /// </summary>
    public async ValueTask<List<Channel>> GetVoiceChannelsAsync(bool refresh = false)
    {
        if (!VoiceChannels.Initialized || refresh)
            await LoadChannelsAsync();

        return VoiceChannels.GetContents();
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
            await ValourCache.Put(info.Member.Id, info.Member, true);
            await ValourCache.Put((info.Member.PlanetId, info.Member.UserId), info.Member, true);
            await ValourCache.Put(info.Member.UserId, info.User, true);
        }

        foreach (var info in allResults)
        {
            var member = ValourCache.Get<PlanetMember>(info.Member.Id);

            if (member is not null)
                Members.Add(member);
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
            await ValourCache.Put(invite.Id, invite, true);
            await ValourCache.Put(invite.Code, invite, true);
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
            await ValourCache.Put(role.Id, role, true);
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

    public Task<PagedModelResponse<EcoAccount>> GetPlanetAccounts(int skip = 0, int take = 50) =>
        EcoAccount.GetPlanetPlanetAccountsAsync(Id);
}
