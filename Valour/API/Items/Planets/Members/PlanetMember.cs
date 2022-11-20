using Valour.Api.Client;
using Valour.Api.Items.Users;
using Valour.Api.Nodes;
using Valour.Shared.Items.Planets.Members;

namespace Valour.Api.Items.Planets.Members;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMember : Item, IPlanetItem, ISharedPlanetMember
{
    #region IPlanetItem implementation

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
        IPlanetItem.GetPlanetAsync(this, refresh);

    public override string BaseRoute =>
            $"api/{nameof(Planet)}/{PlanetId}/{nameof(PlanetMember)}";

    #endregion

    public const int FLAG_UPDATE_ROLES = 0x01;

    /// <summary>
    /// Cached roles
    /// </summary>
    private List<PlanetRole> Roles = null;

    /// <summary>
    /// The user within the planet
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// The name to be used within the planet
    /// </summary>
    public string Nickname { get; set; }

    /// <summary>
    /// The pfp to be used within the planet
    /// </summary>
    public string MemberPfp { get; set; }

    /// <summary>
    /// Returns the member for the given id
    /// </summary>
    public static async ValueTask<PlanetMember> FindAsync(long id, long planetId, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PlanetMember>(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var member = (await node.GetJsonAsync<PlanetMember>($"api/{nameof(Planet)}/{planetId}/{nameof(PlanetMember)}/{id}")).Data;

        if (member is not null)
            await member.AddToCache();

        return member;
    }

    public override async Task AddToCache()
    {
        await ValourCache.Put(Id, this);
        await ValourCache.Put((PlanetId, UserId), this);
    }

    /// <summary>
    /// Returns the member for the given id
    /// </summary>
    public static async ValueTask<PlanetMember> FindAsyncByUser(long userId, long planetId, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PlanetMember>((planetId, userId));
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var member = (await node.GetJsonAsync<PlanetMember>($"api/{nameof(Planet)}/{planetId}/{nameof(PlanetMember)}/byuser/{userId}")).Data;

        if (member is not null)
        {
            await ValourCache.Put(member.Id, member);
            await ValourCache.Put((planetId, userId), member);
        }

        return member;
    }

    /// <summary>
    /// Returns the primary role of this member
    /// </summary>
    public async ValueTask<PlanetRole> GetPrimaryRoleAsync(bool force_refresh = false)
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
    public async ValueTask<List<PlanetRole>> GetRolesAsync(bool force_refresh = false)
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
    public async ValueTask<bool> HasRoleAsync(long id, bool force_refresh = false)
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
    public ValueTask<bool> HasRoleAsync(PlanetRole role, bool force_refresh = false) =>
        HasRoleAsync(role.Id, force_refresh);
    
    /// <summary>
    /// Returns the authority of the member
    /// </summary>
    public async Task<int> GetAuthorityAsync() =>
        (await Node.GetJsonAsync<int>($"{IdRoute}/authority")).Data;

    /// <summary>
    /// Loads all role Ids from the server
    /// </summary>
    public async Task LoadRolesAsync(List<long> roleIds = null)
    {
        if (roleIds is null)
            roleIds = (await Node.GetJsonAsync<List<long>>($"{IdRoute}/roles")).Data;

        if (Roles is null)
            Roles = new List<PlanetRole>();
        else
            Roles.Clear();

        foreach (var id in roleIds)
        {
            var role = await PlanetRole.FindAsync(id, PlanetId);

            if (role is not null)
                Roles.Add(role);
        }

        Roles.Sort((a, b) => a.Position.CompareTo(b.Position));
    }

    /// <summary>
    /// Sets the role Ids manually. This exists for optimization purposes, and you probably shouldn't use it.
    /// It will NOT change anything on the server.
    /// </summary>
    public async Task SetLocalRoleIds(List<long> ids) =>
        await LoadRolesAsync(ids);

    /// <summary>
    /// Returns the user of the member
    /// </summary>
    public ValueTask<User> GetUserAsync(bool force_refresh = false) =>
        User.FindAsync(UserId, force_refresh);

    /// <summary>
    /// Returns the status of the member
    /// </summary>
    public async Task<string> GetStatusAsync(bool force_refresh = false) =>
        (await GetUserAsync(force_refresh))?.Status ?? "";


    /// <summary>
    /// Returns the role color of the member
    /// </summary>
    public async Task<string> GetRoleColorAsync(bool force_refresh = false) =>
        (await GetPrimaryRoleAsync(force_refresh))?.GetColorHex() ?? "#ffffff";
    

    /// <summary>
    /// Returns the pfp url of the member
    /// </summary>
    public async ValueTask<string> GetPfpUrlAsync(bool force_refresh = false)
    {
        if (!string.IsNullOrWhiteSpace(MemberPfp))
            return MemberPfp;

        return (await GetUserAsync(force_refresh))?.PfpUrl ?? "/media/icon-512.png";
    }

    /// <summary>
    /// Returns the name of the member
    /// </summary>
    public async ValueTask<string> GetNameAsync(bool force_refresh = false)
    {
        if (!string.IsNullOrWhiteSpace(Nickname))
            return Nickname;

        return (await GetUserAsync(force_refresh))?.Name ?? "User not found";
    }
}

