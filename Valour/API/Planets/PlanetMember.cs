using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Api.Client;
using Valour.Api.Users;
using Valour.API.Client;
using Valour.Shared;

namespace Valour.Api.Planets;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMember : Shared.Planets.PlanetMember
{
    /// <summary>
    /// Cached roles
    /// </summary>
    private List<ulong> _roleids = null;

    /// <summary>
    /// Returns the member for the given id
    /// </summary>
    public static async Task<TaskResult<PlanetMember>> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh && ValourCache.Members.ContainsKey(id))
            return new TaskResult<PlanetMember>(true, "Success: Cached", ValourCache.Members[id]);

        var getResponse = await ValourClient.GetJsonAsync<PlanetMember>($"api/member/{id}");

        if (getResponse.Success)
        {
            ValourCache.Members[id] = getResponse.Data;
            ValourCache.Members_DualId[(getResponse.Data.Planet_Id, getResponse.Data.User_Id)] = getResponse.Data;
        }

        return getResponse;
    }

    /// <summary>
    /// Returns the member for the given ids
    /// </summary>
    public static async Task<TaskResult<PlanetMember>> FindAsync(ulong planet_id, ulong user_id, bool force_refresh = false)
    {
        if (!force_refresh && ValourCache.Members_DualId.ContainsKey((planet_id, user_id)))
            return new TaskResult<PlanetMember>(true, "Success: Cached", ValourCache.Members_DualId[(planet_id, user_id)]);

        var getResponse = await ValourClient.GetJsonAsync<PlanetMember>($"api/member/{planet_id}/{user_id}");

        if (getResponse.Success)
        {
            ValourCache.Members[getResponse.Data.Id] = getResponse.Data;
            ValourCache.Members_DualId[(planet_id, user_id)] = getResponse.Data;
        }

        return getResponse;
    }

    /// <summary>
    /// Returns the primary role of this member
    /// </summary>
    public async Task<TaskResult<PlanetRole>> GetPrimaryRoleAsync(bool force_refresh = false)
    {
        if (_roleids is null || force_refresh)
        {
            var loadRes = await LoadRoleIdsAsync();
            if (!loadRes.Success)
                return new TaskResult<PlanetRole>(false, loadRes.Message);
        }
            

        if (_roleids.Count > 0)
            return await PlanetRole.FindAsync(_roleids[0], force_refresh);

        return new TaskResult<PlanetRole>(false, "No roles found");
    }

    /// <summary>
    /// Returns the roles of this member
    /// </summary>
    public async Task<TaskResult<List<PlanetRole>>> GetRolesAsync(bool force_refresh = false)
    {
        List<PlanetRole> roles = new List<PlanetRole>();

        if (_roleids is null || force_refresh)
        {
            var loadRes = await LoadRoleIdsAsync();
            if (!loadRes.Success)
                return new TaskResult<List<PlanetRole>>(false, loadRes.Message);
        }

        foreach (var roleid in _roleids)
        {
            var roleRes = await PlanetRole.FindAsync(roleid, force_refresh);

            if (roleRes.Success)
                roles.Add(roleRes.Data);
            else
                return new TaskResult<List<PlanetRole>>(false, roleRes.Message);
        }

        return new TaskResult<List<PlanetRole>>(true, "Success", roles);
    }

    /// <summary>
    /// Returns if the member has the given role
    /// </summary>
    public async Task<TaskResult<bool>> HasRoleAsync(ulong id, bool force_refresh = false)
    {
        if (_roleids is null || force_refresh)
        {
            var loadRes = await LoadRoleIdsAsync();
            if (!loadRes.Success)
                return new TaskResult<bool>(false, loadRes.Message);
        }

        return new TaskResult<bool>(true, "Success", _roleids.Contains(id));
    }

    /// <summary>
    /// Loads all role Ids from the server
    /// </summary>
    public async Task<TaskResult> LoadRoleIdsAsync()
    {
        var result = await ValourClient.GetJsonAsync<List<ulong>>($"api/member/{Id}/role_ids");

        if (result.Success)
        {
            _roleids = result.Data;
            return new TaskResult(true, "Success: Fetched");
        }
        else
        {
            return new TaskResult(false, result.Message);
        }
    }

    /// <summary>
    /// Sets the role Ids manually. This exists for optimization purposes, and you probably shouldn't use it.
    /// It will NOT change anything on the server.
    /// </summary>
    public void SetLocalRoleIds(List<ulong> ids)
    {
        _roleids = ids;
    }

    /// <summary>
    /// Returns the user of the member
    /// </summary>
    public async Task<TaskResult<User>> GetUserAsync(bool force_refresh = false)
    {
        return await User.FindAsync(User_Id, force_refresh);
    }

    /// <summary>
    /// Returns the status of the member
    /// </summary>
    public async Task<TaskResult<string>> GetStatusAsync(bool force_refresh)
    {
        var res = await GetUserAsync(force_refresh);

        if (!res.Success)
            return new TaskResult<string>(false, res.Message);

        return new TaskResult<string>(true, res.Message, res.Data.Status);
    }

    /// <summary>
    /// Returns the role color of the member
    /// </summary>
    public async Task<TaskResult<string>> GetRoleColorAsync(bool force_refresh)
    {
        var res = await GetPrimaryRoleAsync(force_refresh);

        if (!res.Success)
            return new TaskResult<string>(false, res.Message);

        return new TaskResult<string>(true, res.Message, res.Data.GetColorHex());
    }

    /// <summary>
    /// Returns the pfp url of the member
    /// </summary>
    public async Task<TaskResult<string>> GetPfpUrlAsync(bool force_refresh)
    {
        if (!string.IsNullOrWhiteSpace(Nickname))
            return new TaskResult<string>(true, "Success", Nickname);

        var res = await GetUserAsync(force_refresh);

        if (!res.Success)
            return new TaskResult<string>(false, res.Message);

        return new TaskResult<string>(true, res.Message, res.Data.Username);
    }

    /// <summary>
    /// Returns the name of the member
    /// </summary>
    public async Task<TaskResult<string>> GetNameAsync(bool force_refresh)
    {
        if (!string.IsNullOrWhiteSpace(Member_Pfp))
            return new TaskResult<string>(true, "Success", Member_Pfp);

        var res = await GetUserAsync(force_refresh);

        if (!res.Success)
            return new TaskResult<string>(false, res.Message);

        return new TaskResult<string>(true, res.Message, res.Data.Pfp_Url);
    }
}

/// <summary>
/// For getting data from the server.  Must match the one in Shared!
/// </summary>
public class PlanetMemberInfo
{
    [JsonPropertyName("Member")]
    public PlanetMember Member { get; set; }

    [JsonPropertyName("RoleIds")]
    public List<ulong> RoleIds { get; set; }

    [JsonPropertyName("User")]
    public User User { get; set; }
}

