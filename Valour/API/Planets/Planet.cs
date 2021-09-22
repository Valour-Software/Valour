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
        if (!force_refresh)
        {
            var cached = ValourCache.Get<Planet>(id);
            if (cached is not null)
                return new TaskResult<Planet>(true, "Success: Cached", cached);
        }

        var res = await ValourClient.GetJsonAsync<Planet>($"api/planet/{id}");

        if (res.Success)
            ValourCache.Put(id, res.Data);

        return res;
    }

    /// <summary>
    /// Returns the primary channel of the planet
    /// </summary>
    public async Task<TaskResult<Channel>> GetPrimaryChannelAsync(bool force_refresh = false)
    {
        if (_channel_ids == null || force_refresh)
        {
            var res = await LoadChannelsAsync();

            if (!res.Success)
                return new TaskResult<Channel>(false, res.Message);
        }

        return await Channel.FindAsync(Main_Channel_Id, force_refresh);
    }

    /// <summary>
    /// Returns the categories of this planet
    /// </summary>
    public async Task<TaskResult<List<Category>>> GetCategoriesAsync(bool force_refresh = false)
    {
        if (_category_ids == null || force_refresh)
        {
            var res = await LoadCategoriesAsync();

            if (!res.Success)
                return new TaskResult<List<Category>>(false, res.Message);
        }

        List<Category> categories = new();

        foreach (var id in _category_ids)
        {
            var catResult = await Category.FindAsync(id);

            if (!catResult.Success)
                return new TaskResult<List<Category>>(false, catResult.Message);

            categories.Add(catResult.Data);
        }

        return new TaskResult<List<Category>>(true, "Success", categories);
    }

    /// <summary>
    /// Requests and caches categories from the server
    /// </summary>
    public async Task<TaskResult> LoadCategoriesAsync()
    {
        var result = await ValourClient.GetJsonAsync<List<Category>>($"api/planet/{Id}/categories");

        if (result.Success)
        {
            foreach (var category in result.Data)
            {
                ValourCache.Put(category.Id, category);
            }

            _category_ids = result.Data.OrderBy(x => x.Position).Select(x => x.Id).ToList();

            return new TaskResult(true, result.Message);
        }

        return new TaskResult(false, result.Message);
    }

    /// <summary>
    /// Returns the channels of a planet
    /// </summary>
    public async Task<TaskResult<List<Channel>>> GetChannelsAsync(bool force_refresh = false)
    {
        if (_channel_ids == null || force_refresh)
        {
            var res = await LoadChannelsAsync();

            if (!res.Success)
                return new TaskResult<List<Channel>>(false, res.Message);
        }

        List<Channel> channels = new();

        foreach (var id in _channel_ids)
        {
            var chanRes = await Channel.FindAsync(id);

            if (!chanRes.Success)
                return new TaskResult<List<Channel>>(false, chanRes.Message);

            channels.Add(chanRes.Data);
        }

        return new TaskResult<List<Channel>>(true, "Success", channels);
    }

    /// <summary>
    /// Requests and caches channels from the server
    /// </summary>
    public async Task<TaskResult> LoadChannelsAsync()
    {
        var result = await ValourClient.GetJsonAsync<List<Channel>>($"/api/planet/{Id}/channels");

        if (result.Success)
        {
            foreach (var channel in result.Data)
            {
                ValourCache.Put(channel.Id, channel);
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
    public async Task<TaskResult<List<Member>>> GetMembersAsync(bool force_refresh)
    {
        if (_member_ids is null || force_refresh)
        {
            var res = await LoadMemberDataAsync();

            if (!res.Success)
                return new TaskResult<List<Member>>(false, res.Message);
        }

        List<Member> members = new List<Member>();

        foreach (var id in _member_ids)
        {
            var res = await Member.FindAsync(id);

            if (!res.Success)
                return new TaskResult<List<Member>>(false, res.Message);

            members.Add(res.Data);
        }

        return new TaskResult<List<Member>>(true, "Success", members);
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
            ValourCache.Put(info.Member.Id, info.Member);
            ValourCache.Put((info.Member.Planet_Id, info.Member.User_Id), info.Member);
            ValourCache.Put(info.Member.User_Id, info.User);

            _member_ids.Add(info.Member.Id);
        }

        return new TaskResult(true, "Success");
    }

    /// <summary>
    /// Returns the roles of a planet
    /// </summary>
    public async Task<TaskResult<List<Role>>> GetRolesAsync(bool force_refresh = false)
    {
        if (_role_ids is null || force_refresh)
        {
            var res = await LoadRolesAsync();

            if (!res.Success)
                return new TaskResult<List<Role>>(false, res.Message);
        }

        List<Role> roles = new();

        foreach (var id in _role_ids)
        {
            var res = await Role.FindAsync(id, force_refresh);

            if (!res.Success)
                return new TaskResult<List<Role>>(false, res.Message);

            roles.Add(res.Data);
        }

        return new TaskResult<List<Role>>(true, "Success", roles);
    }

    /// <summary>
    /// Loads the roles of a planet from the server
    /// </summary>
    public async Task<TaskResult> LoadRolesAsync()
    {
        var result = await ValourClient.GetJsonAsync<List<Role>>($"api/planet/{Id}/roles");

        if (result.Success)
        {
            foreach (var role in result.Data)
            {
                ValourCache.Put(role.Id, role);
            }

            _role_ids = result.Data.OrderBy(x => x.Position).Select(x => x.Id).ToList();

            return new TaskResult(true, result.Message);
        }

        return new TaskResult(false, result.Message);
    }

    /// <summary>
    /// Returns the member for a given user id
    /// </summary>
    public async Task<TaskResult<Member>> GetMemberAsync(ulong user_id, bool force_refresh = false)
    {
        return await Member.FindAsync(Id, user_id, force_refresh);
    }

    /// <summary>
    /// Ran to notify the planet that a channel has been updated
    /// </summary>
    public async Task NotifyUpdateChannel(Channel channel)
    {
        if (_channel_ids == null)
            await LoadChannelsAsync();

        // Set in cache
        ValourCache.Put(channel.Id, channel);

        // Re-order channels
        List<Channel> channels = new();

        foreach (var id in _channel_ids)
        {
            channels.Add(ValourCache.Get<Channel>(id));
        }

        _channel_ids = channels.OrderBy(x => x.Position).Select(x => x.Id).ToList();
    }

    /// <summary>
    /// Ran to notify the planet that a channel has been deleted
    /// </summary>
    public void NotifyDeleteChannel(Channel channel)
    {
        _channel_ids.Remove(channel.Id);

        ValourCache.Remove<Channel>(channel.Id);
    }

    /// <summary>
    /// Ran to notify the planet that a category has been updated
    /// </summary>
    public async Task NotifyUpdateCategory(Category category)
    {
        if (_category_ids == null)
            await LoadCategoriesAsync();

        // Set in cache
        ValourCache.Put(category.Id, category);

        // Reo-order categories
        List<Category> categories = new();

        foreach (var id in _category_ids)
        {
            categories.Add(ValourCache.Get<Category>(id));
        }

        _category_ids = categories.OrderBy(x => x.Position).Select(x => x.Id).ToList();
    }

    /// <summary>
    /// Ran to notify the planet that a category has been deleted
    /// </summary>
    public void NotifyDeleteCategory(Category category)
    {
        _category_ids.Remove(category.Id);

        ValourCache.Remove<Category>(category.Id);
    }

}
