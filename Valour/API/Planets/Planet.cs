using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using Valour.Client.Channels;
using Valour.Client.Categories;
using System.Linq;
using System.Text.Json;
using System.Net.Http.Json;
using Valour.Api.Client;
using Valour.API.Client;
using Valour.Shared;

namespace Valour.Api.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */
public class Planet : Shared.Planets.Planet
{
    // Cached values
    private List<ulong> _channel_ids = null;
    private List<ulong> _category_ids = null;
    private List<ulong> _role_ids = null;
    private List<ulong> _member_ids = null;

    /// <summary>
    /// Retrieves and returns a client planet by requesting from the server
    /// </summary>
    public static async Task<TaskResult<Planet>> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh && ValourCache.Planets.ContainsKey(id))
            return new TaskResult<Planet>(true, "Success: Cached", ValourCache.Planets[id]);

        var res = await ValourClient.GetJsonAsync<Planet>($"api/planet/{id}");

        if (res.Success)
            ValourCache.Planets[id] = res.Data;

        return res;
    }

    /// <summary>
    /// Returns the primary channel of the planet
    /// </summary>
    public async Task<TaskResult<ClientPlanetChatChannel>> GetPrimaryChannelAsync(bool force_refresh = false)
    {
        if (_channel_ids == null || force_refresh)
        {
            var res = await LoadChannelsAsync();

            if (!res.Success)
                return new TaskResult<ClientPlanetChatChannel>(false, res.Message);
        }

        return new TaskResult<ClientPlanetChatChannel>(true, "Success", ValourCache.Channels[Main_Channel_Id]);
    }

    /// <summary>
    /// Returns the categories of this planet
    /// </summary>
    public async Task<TaskResult<List<ClientPlanetCategory>>> GetCategoriesAsync(bool force_refresh = false)
    {
        if (_category_ids == null || force_refresh)
        {
            var res = await LoadCategoriesAsync();

            if (!res.Success)
                return new TaskResult<List<ClientPlanetCategory>>(false, res.Message);
        }

        List<ClientPlanetCategory> categories = new();

        foreach (var id in _category_ids)
            categories.Add(ValourCache.Categories[id]);

        return new TaskResult<List<ClientPlanetCategory>>(true, "Success", categories);
    }

    /// <summary>
    /// Requests and caches categories from the server
    /// </summary>
    public async Task<TaskResult> LoadCategoriesAsync()
    {
        var result = await ValourClient.GetJsonAsync<List<ClientPlanetCategory>>($"api/planet/{Id}/categories");

        if (result.Success)
        {
            foreach (var category in result.Data)
            {
                ValourCache.Categories[category.Id] = category;
            }

            _category_ids = result.Data.OrderBy(x => x.Position).Select(x => x.Id).ToList();

            return new TaskResult(true, result.Message);
        }

        return new TaskResult(false, result.Message);
    }

    /// <summary>
    /// Returns the channels of a planet
    /// </summary>
    public async Task<TaskResult<List<ClientPlanetChatChannel>>> GetChannelsAsync(bool force_refresh = false)
    {
        if (_channel_ids == null || force_refresh)
        {
            var res = await LoadChannelsAsync();

            if (!res.Success)
                return new TaskResult<List<ClientPlanetChatChannel>>(false, res.Message);
        }

        List<ClientPlanetChatChannel> channels = new();

        foreach (var id in _channel_ids)
            // TODO: this
            channels.Add(ValourCache.Channels[id]);

        return new TaskResult<List<ClientPlanetChatChannel>>(true, "Success", channels);
    }

    /// <summary>
    /// Requests and caches channels from the server
    /// </summary>
    public async Task<TaskResult> LoadChannelsAsync()
    {
        var result = await ValourClient.GetJsonAsync<List<ClientPlanetChatChannel>>($"/api/planet/{Id}/channels");

        if (result.Success)
        {
            foreach (var channel in result.Data)
            {
                ValourCache.Channels[channel.Id] = channel;
            }

            _channel_ids = result.Data.OrderBy(x => x.Position).Select(x => x.Id).ToList();

            return new TaskResult(true, result.Message);
        }

        return new TaskResult(false, result.Message);
    }

    /// <summary>
    /// Attempts to set the name of the planet
    /// </summary>
    public async Task<TaskResult> TrySetNameAsync(string name)
    {
        var result = await ValourClient.PutAsync($"api/planet/{Id}/name", name);

        if (result.Success)
            Name = name;

        return result;
    }

    /// <summary>
    /// Attempts to set the description of the planet
    /// </summary>
    public async Task<TaskResult> TrySetDescriptionAsync(string description)
    {
        var result = await ValourClient.PutAsync($"api/planet/{Id}/description", description);

        if (result.Success)
            Description = description;

        return result;
    }

    /// <summary>
    /// Attempts to set the public value of the planet
    /// </summary>
    public async Task<TaskResult> SetPublic(bool is_public)
    {
        var result = await ValourClient.PutAsync($"api/planet/{Id}/public", is_public);

        if (result.Success)
            Public = is_public;

        return result;
    }

    /// <summary>
    /// Returns the members of the planet
    /// </summary>
    public async Task<TaskResult<List<PlanetMember>>> GetMembersAsync(bool force_refresh)
    {
        if (_member_ids is null || force_refresh)
        {
            var res = await LoadMemberDataAsync();

            if (!res.Success)
                return new TaskResult<List<PlanetMember>>(false, res.Message);
        }

        List<PlanetMember> members = new List<PlanetMember>();

        foreach (var id in _member_ids)
        {
            var res = await PlanetMember.FindAsync(id);

            if (!res.Success)
                return new TaskResult<List<PlanetMember>>(false, res.Message);

            members.Add(res.Data);
        }

        return new TaskResult<List<PlanetMember>>(true, "Success", members);
    }

    /// <summary>
    /// Loads the member data for the planet (this is quite heavy) 
    /// </summary>
    public async Task<TaskResult> LoadMemberDataAsync()
    {
        var result = await ValourClient.GetJsonAsync<List<PlanetMemberInfo>>($"api/planet/{Id}/member_info");

        if (!result.Success)
            return new TaskResult(false, result.Message);

        if (_member_ids is null)
            _member_ids = new List<ulong>();
        else
            _member_ids.Clear();

        foreach (var info in result.Data)
        {
            // Set role id data manually
            info.Member.SetLocalRoleIds(info.RoleIds);

            // Set in cache
            ValourCache.Members[info.Member.Id] = info.Member;
            ValourCache.Members_DualId[(info.Member.Planet_Id, info.Member.User_Id)] = info.Member;
            ValourCache.Users[info.User.Id] = info.User;

            _member_ids.Add(info.Member.Id);
        }

        return new TaskResult(true, "Success");
    }

    /// <summary>
    /// Returns the roles of a planet
    /// </summary>
    public async Task<TaskResult<List<PlanetRole>>> GetRolesAsync(bool force_refresh = false)
    {
        if (_role_ids is null || force_refresh)
        {
            var res = await LoadRolesAsync();

            if (!res.Success)
                return new TaskResult<List<PlanetRole>>(false, res.Message);
        }

        List<PlanetRole> roles = new();

        foreach (var id in _role_ids)
        {
            var res = await PlanetRole.FindAsync(id, force_refresh);

            if (!res.Success)
                return new TaskResult<List<PlanetRole>>(false, res.Message);

            roles.Add(res.Data);
        }

        return new TaskResult<List<PlanetRole>>(true, "Success", roles);
    }

    /// <summary>
    /// Loads the roles of a planet from the server
    /// </summary>
    public async Task<TaskResult> LoadRolesAsync()
    {
        var result = await ValourClient.GetJsonAsync<List<PlanetRole>>($"api/planet/{Id}/roles");

        if (result.Success)
        {
            foreach (var role in result.Data)
            {
                ValourCache.Roles[role.Id] = role;
            }

            _role_ids = result.Data.OrderBy(x => x.Position).Select(x => x.Id).ToList();

            return new TaskResult(true, result.Message);
        }

        return new TaskResult(false, result.Message);
    }

    /// <summary>
    /// Returns the member for a given user id
    /// </summary>
    public async Task<TaskResult<PlanetMember>> GetMemberAsync(ulong user_id, bool force_refresh = false)
    {
        return await PlanetMember.FindAsync(Id, user_id, force_refresh);
    }

    /// <summary>
    /// Ran to notify the planet that a channel has been updated
    /// </summary>
    public async Task NotifyUpdateChannel(ClientPlanetChatChannel channel)
    {
        if (_channel_ids == null)
            await LoadChannelsAsync();

        // Set in cache
        ValourCache.Channels[channel.Id] = channel;

        // Re-order channels
        List<ClientPlanetChatChannel> channels = new();

        foreach (var id in _channel_ids)
        {
            channels.Add(ValourCache.Channels[id]);
        }

        _channel_ids = channels.OrderBy(x => x.Position).Select(x => x.Id).ToList();
    }

    /// <summary>
    /// Ran to notify the planet that a channel has been deleted
    /// </summary>
    public void NotifyDeleteChannel(ClientPlanetChatChannel channel)
    {
        _channel_ids.Remove(channel.Id);

        ValourCache.Channels.Remove(channel.Id, out _);
    }

    /// <summary>
    /// Ran to notify the planet that a category has been updated
    /// </summary>
    public async Task NotifyUpdateCategory(ClientPlanetCategory category)
    {
        if (_category_ids == null)
            await LoadCategoriesAsync();

        // Set in cache
        ValourCache.Categories[category.Id] = category;

        // Reo-order categories
        List<ClientPlanetCategory> categories = new();

        foreach (var id in _category_ids)
        {
            categories.Add(ValourCache.Categories[id]);
        }

        _category_ids = categories.OrderBy(x => x.Position).Select(x => x.Id).ToList();
    }

    /// <summary>
    /// Ran to notify the planet that a category has been deleted
    /// </summary>
    public void NotifyDeleteCategory(ClientPlanetCategory category)
    {
        _category_ids.Remove(category.Id);

        ValourCache.Categories.Remove(category.Id, out _);
    }

}
