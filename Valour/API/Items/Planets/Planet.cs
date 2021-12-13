using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Planets.Channels;
using Valour.Api.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

namespace Valour.Api.Items.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */
public class Planet : NamedItem<Planet>, ISharedPlanet
{
    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    [JsonPropertyName("Owner_Id")]
    public ulong Owner_Id { get; set; }

    /// <summary>
    /// The image url for the planet 
    /// </summary>
    [JsonPropertyName("Image_Url")]
    public string Image_Url { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    [JsonPropertyName("Description")]
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    [JsonPropertyName("Public")]
    public bool Public { get; set; }

    /// <summary>
    /// The amount of members on the planet
    /// </summary>
    [JsonPropertyName("Member_Count")]
    public uint Member_Count { get; set; }

    /// <summary>
    /// The default role for the planet
    /// </summary>
    [JsonPropertyName("Default_Role_Id")]
    public ulong Default_Role_Id { get; set; }

    /// <summary>
    /// The id of the main channel of the planet
    /// </summary>
    [JsonPropertyName("Main_Channel_Id")]
    public ulong Main_Channel_Id { get; set; }

    /// <summary>
    /// The item type of this item
    /// </summary>
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.Planet;

    // Cached values
    private List<PlanetChatChannel> Channels { get; set; }
    private List<PlanetCategory> Categories { get; set; }
    private List<PlanetRole> Roles { get; set; }
    private List<PlanetMember> Members { get; set; }

    /// <summary>
    /// Retrieves and returns a client planet by requesting from the server
    /// </summary>
    public static async Task<Planet> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<Planet>(id);
            if (cached is not null)
                return cached;
        }

        var planet = await ValourClient.GetJsonAsync<Planet>($"api/planet/{id}");

        if (planet is not null)
            await ValourCache.Put(id, planet);

        return planet;
    }

    /// <summary>
    /// Returns the primary channel of the planet
    /// </summary>
    public async Task<PlanetChatChannel> GetPrimaryChannelAsync(bool force_refresh = false)
    {
        if (Channels == null || force_refresh)
        {
            await LoadChannelsAsync();
        }

        return await PlanetChatChannel.FindAsync(Main_Channel_Id, force_refresh);
    }

    /// <summary>
    /// Returns the categories of this planet
    /// </summary>
    public async Task<List<PlanetCategory>> GetCategoriesAsync(bool force_refresh = false)
    {
        if (Categories == null || force_refresh)
        {
            await LoadCategoriesAsync();
        }

        return Categories;
    }

    /// <summary>
    /// Requests and caches categories from the server
    /// </summary>
    public async Task LoadCategoriesAsync()
    {
        var categories = await ValourClient.GetJsonAsync<List<PlanetCategory>>($"api/planet/{Id}/categories");

        if (categories is null)
            return;

        // Update cache values
        foreach (var category in categories)
        {
            // Skip event for bulk loading
            await ValourCache.Put(category.Id, category, true);
        }

        // Create container if needed
        if (Categories == null)
            Categories = new List<PlanetCategory>();
        else
            Categories.Clear();

        // Retrieve cache values (this is necessary to ensure single copies of items)
        foreach (var category in categories)
        {
            var cCat = ValourCache.Get<PlanetCategory>(category.Id);

            if (cCat is not null)
                Categories.Add(cCat);
        }

        // Sort via position
        Categories.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Returns the channels of a planet
    /// </summary>
    public async Task<List<PlanetChatChannel>> GetChannelsAsync(bool force_refresh = false)
    {
        if (Channels == null || force_refresh)
        {
            await LoadChannelsAsync();
        }

        return Channels;
    }

    /// <summary>
    /// Requests and caches channels from the server
    /// </summary>
    public async Task LoadChannelsAsync()
    {
        var channels = await ValourClient.GetJsonAsync<List<PlanetChatChannel>>($"/api/planet/{Id}/channels");

        if (channels is null)
            return;

        foreach (var channel in channels)
        {
            // Skip event for bulk loading
            await ValourCache.Put(channel.Id, channel, true);
        }

        // Create container if needed
        if (Channels == null)
            Channels = new List<PlanetChatChannel>();
        else
            Channels.Clear();

        // Retrieve cache values (this is necessary to ensure single copies of items)
        foreach (var channel in channels)
        {
            var cChan = ValourCache.Get<PlanetChatChannel>(channel.Id);

            if (cChan is not null)
                Channels.Add(cChan);
        }

        // Sort via position
        Channels.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Attempts to set the name of the planet
    /// </summary>
    public async Task<TaskResult> TrySetNameAsync(string name) =>
        await ValourClient.PutAsync($"api/planet/{Id}/name", name);

    /// <summary>
    /// Attempts to set the description of the planet
    /// </summary>
    public async Task<TaskResult> TrySetDescriptionAsync(string description) =>
        await ValourClient.PutAsync($"api/planet/{Id}/description", description);

    /// <summary>
    /// Attempts to set the public value of the planet
    /// </summary>
    public async Task<TaskResult> SetPublic(bool is_public) =>
        await ValourClient.PutAsync($"api/planet/{Id}/public", is_public);

    /// <summary>
    /// Returns the members of the planet
    /// </summary>
    public async Task<List<PlanetMember>> GetMembersAsync(bool force_refresh = false)
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
        var result = await ValourClient.GetJsonAsync<List<PlanetMemberInfo>>($"api/planet/{Id}/member_info");

        if (Members is null)
            Members = new List<PlanetMember>();
        else
            Members.Clear();

        foreach (var info in result)
        {
            // Set role id data manually
            await info.Member.SetLocalRoleIds(info.RoleIds);

            // Set in cache
            // Skip event for bulk loading
            await ValourCache.Put(info.Member.Id, info.Member, true);
            await ValourCache.Put((info.Member.Planet_Id, info.Member.User_Id), info.Member, true);
            await ValourCache.Put(info.Member.User_Id, info.User, true);
        }

        foreach (var info in result)
        {
            var member = ValourCache.Get<PlanetMember>(info.Member.Id);

            if (member is not null)
                Members.Add(member);
        }
    }

    /// <summary>
    /// Returns the roles of a planet
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(bool force_refresh = false)
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
        var roles = await ValourClient.GetJsonAsync<List<PlanetRole>>($"api/planet/{Id}/roles");

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
            var cRole = await PlanetRole.FindAsync(role.Id);

            if (cRole is not null)
                Roles.Add(cRole);
        }

        Roles.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Returns the member for a given user id
    /// </summary>
    public async Task<PlanetMember> GetMemberAsync(ulong user_id, bool force_refresh = false)
    {
        return await PlanetMember.FindAsync(Id, user_id, force_refresh);
    }

    /// <summary>
    /// Ran to notify the planet that a channel has been updated
    /// </summary>
    public async Task NotifyUpdateChannel(PlanetChatChannel channel)
    {
        if (Channels == null)
            await LoadChannelsAsync();

        if (!Channels.Contains(channel))
            return;

        // Resort
        Channels.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Ran to notify the planet that a channel has been deleted
    /// </summary>
    public async Task NotifyDeleteChannel(PlanetChatChannel channel)
    {
        if (Channels == null)
            await LoadChannelsAsync();

        if (!Channels.Contains(channel))
            return;

        Channels.Remove(channel);
    }

    /// <summary>
    /// Ran to notify the planet that a category has been updated
    /// </summary>
    public async Task NotifyUpdateCategory(PlanetCategory category)
    {
        if (Categories == null)
            await LoadCategoriesAsync();

        if (!Categories.Contains(category))
            return;

        // Resort
        Categories.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Ran to notify the planet that a category has been deleted
    /// </summary>
    public async Task NotifyDeleteCategory(PlanetCategory category)
    {
        if (Categories == null)
            await LoadCategoriesAsync();

        if (!Categories.Contains(category))
            return;

        Categories.Remove(category);
    }

    /// <summary>
    /// Ran to notify the planet that a role has been updated
    /// </summary>
    public async Task NotifyUpdateRole(PlanetRole role)
    {
        if (Roles == null)
            await LoadRolesAsync();

        if (!Roles.Contains(role))
            return;

        // Resort
        Roles.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Ran to notify the planet that a role has been deleted
    /// </summary>
    public async Task NotifyDeleteRole(PlanetRole role)
    {
        if (Roles == null)
            await LoadRolesAsync();

        if (!Roles.Contains(role))
            return;

        // Resort
        Roles.Remove(role);
    }

}
