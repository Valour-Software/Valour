using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Users;

namespace Valour.Api.Items.Planets.Members;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMember : Shared.Planets.PlanetMember<PlanetMember>
{
    /// <summary>
    /// Cached roles
    /// </summary>
    private List<PlanetRole> Roles = null;

    public override async Task OnUpdate(int flags)
    {
        if ((flags & FLAG_UPDATE_ROLES) != 0)
            await LoadRolesAsync();
    }

    /// <summary>
    /// Returns the member for the given id
    /// </summary>
    public static async Task<PlanetMember> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PlanetMember>(id);
            if (cached is not null)
                return cached;
        }

        var member = await ValourClient.GetJsonAsync<PlanetMember>($"api/member/{id}");

        if (member is not null)
        {
            await ValourCache.Put(id, member);
            await ValourCache.Put((member.Planet_Id, member.User_Id), member);
        }

        return member;
    }

    /// <summary>
    /// Returns the member for the given ids
    /// </summary>
    public static async Task<PlanetMember> FindAsync(ulong planet_id, ulong user_id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PlanetMember>((planet_id, user_id));
            if (cached is not null)
                return cached;
        }

        var member = await ValourClient.GetJsonAsync<PlanetMember>($"api/member/{planet_id}/{user_id}");

        if (member is not null)
        {
            await ValourCache.Put(member.Id, member);
            await ValourCache.Put((planet_id, user_id), member);
        }

        return member;
    }

    /// <summary>
    /// Returns the primary role of this member
    /// </summary>
    public async Task<PlanetRole> GetPrimaryRoleAsync(bool force_refresh = false)
    {
        if (Roles is null || force_refresh)
        {
            await LoadRolesAsync();
        }

        if (Roles.Count > 0)
            return Roles[0];

        return null;
    }

    /// <summary>
    /// Returns the roles of this member
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
    /// Returns if the member has the given role
    /// </summary>
    public async Task<bool> HasRoleAsync(ulong id, bool force_refresh = false)
    {
        if (Roles is null || force_refresh)
        {
            await LoadRolesAsync();
        }

        return Roles.Any(x => x.Id == id);
    }

    /// <summary>
    /// Returns if the member has the given role
    /// </summary>
    public async Task<bool> HasRoleAsync(PlanetRole role, bool force_refresh = false) =>
        await HasRoleAsync(role.Id, force_refresh);

    /// <summary>
    /// Returns the planet of the member
    /// </summary>
    public async Task<Planet> GetPlanetAsync() =>
        await Planet.FindAsync(Planet_Id);
    
    /// <summary>
    /// Returns the authority of the member
    /// </summary>
    public async Task<ulong> GetAuthorityAsync() =>
        await ValourClient.GetJsonAsync<ulong>($"api/member/{Id}/authority");

    /// <summary>
    /// Loads all role Ids from the server
    /// </summary>
    public async Task LoadRolesAsync(List<ulong> role_ids = null)
    {
        if (role_ids is null)
            role_ids = await ValourClient.GetJsonAsync<List<ulong>>($"api/member/{Id}/role_ids");

        if (Roles is null)
            Roles = new List<PlanetRole>();
        else
            Roles.Clear();

        foreach (var id in role_ids)
        {
            var role = await PlanetRole.FindAsync(id);

            if (role is not null)
                Roles.Add(role);
        }

        Roles.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Sets the role Ids manually. This exists for optimization purposes, and you probably shouldn't use it.
    /// It will NOT change anything on the server.
    /// </summary>
    public async Task SetLocalRoleIds(List<ulong> ids) =>
        await LoadRolesAsync(ids);

    /// <summary>
    /// Returns the user of the member
    /// </summary>
    public async Task<User> GetUserAsync(bool force_refresh = false) =>
        await User.FindAsync(User_Id, force_refresh);

    /// <summary>
    /// Returns the status of the member
    /// </summary>
    public async Task<string> GetStatusAsync(bool force_refresh = false) =>
        (await GetUserAsync(force_refresh))?.Status ?? "";


    /// <summary>
    /// Returns the role color of the member
    /// </summary>
    public async Task<string> GetRoleColorAsync(bool force_refresh = false) =>
        (await GetPrimaryRoleAsync(force_refresh))?.GetColorHex() ?? "ffffff";
    

    /// <summary>
    /// Returns the pfp url of the member
    /// </summary>
    public async Task<string> GetPfpUrlAsync(bool force_refresh = false)
    {
        if (!string.IsNullOrWhiteSpace(Member_Pfp))
            return Member_Pfp;

        return (await GetUserAsync(force_refresh))?.Pfp_Url ?? "/media/icon-512.png";
    }

    /// <summary>
    /// Returns the name of the member
    /// </summary>
    public async Task<string> GetNameAsync(bool force_refresh = false)
    {
        if (!string.IsNullOrWhiteSpace(Nickname))
            return Nickname;

        return (await GetUserAsync(force_refresh))?.Username ?? "User not found";
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

