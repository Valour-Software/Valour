using System.Text.Json.Serialization;
using Valour.Api.Authorization.Roles;
using Valour.Api.Client;
using Valour.Api.Roles;
using Valour.Api.Users;
using Valour.Shared;

namespace Valour.Api.Planets;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class Member : Shared.Planets.PlanetMember
{
    /// <summary>
    /// Cached roles
    /// </summary>
    private List<ulong> _roleids = null;

    /// <summary>
    /// Returns the member for the given id
    /// </summary>
    public static async Task<Member> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<Member>(id);
            if (cached is not null)
                return cached;
        }

        var member = await ValourClient.GetJsonAsync<Member>($"api/member/{id}");

        if (member is not null)
        {
            ValourCache.Put(id, member);
            ValourCache.Put((member.Planet_Id, member.User_Id), member);
        }

        return member;
    }

    /// <summary>
    /// Returns the member for the given ids
    /// </summary>
    public static async Task<Member> FindAsync(ulong planet_id, ulong user_id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<Member>((planet_id, user_id));
            if (cached is not null)
                return cached;
        }

        var member = await ValourClient.GetJsonAsync<Member>($"api/member/{planet_id}/{user_id}");

        if (member is not null)
        {
            ValourCache.Put(member.Id, member);
            ValourCache.Put((planet_id, user_id), member);
        }

        return member;
    }

    /// <summary>
    /// Returns the primary role of this member
    /// </summary>
    public async Task<Role> GetPrimaryRoleAsync(bool force_refresh = false)
    {
        if (_roleids is null || force_refresh)
        {
            await LoadRoleIdsAsync();
        }     

        if (_roleids.Count > 0)
            return await Role.FindAsync(_roleids[0], force_refresh);

        return null;
    }

    /// <summary>
    /// Returns the roles of this member
    /// </summary>
    public async Task<List<Role>> GetRolesAsync(bool force_refresh = false)
    {
        List<Role> roles = new List<Role>();

        if (_roleids is null || force_refresh)
        {
            await LoadRoleIdsAsync();
        }

        foreach (var roleid in _roleids)
        {
            var role = await Role.FindAsync(roleid, force_refresh);

            if (role is not null)
                roles.Add(role);
        }

        return roles;
    }

    /// <summary>
    /// Returns if the member has the given role
    /// </summary>
    public async Task<bool> HasRoleAsync(ulong id, bool force_refresh = false)
    {
        if (_roleids is null || force_refresh)
        {
            await LoadRoleIdsAsync();
        }

        return _roleids.Contains(id);
    }

    /// <summary>
    /// Returns the authority of the member
    /// </summary>
    public async Task<ulong> GetAuthorityAsync() =>
        await ValourClient.GetJsonAsync<ulong>($"api/member/{Id}/authority");

    /// <summary>
    /// Loads all role Ids from the server
    /// </summary>
    public async Task LoadRoleIdsAsync() => 
        _roleids = await ValourClient.GetJsonAsync<List<ulong>>($"api/member/{Id}/role_ids");

    /// <summary>
    /// Sets the role Ids manually. This exists for optimization purposes, and you probably shouldn't use it.
    /// It will NOT change anything on the server.
    /// </summary>
    public void SetLocalRoleIds(List<ulong> ids) =>
        _roleids = ids;

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
    public Member Member { get; set; }

    [JsonPropertyName("RoleIds")]
    public List<ulong> RoleIds { get; set; }

    [JsonPropertyName("User")]
    public User User { get; set; }
}

