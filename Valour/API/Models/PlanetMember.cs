using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Api.Nodes;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Api.Models;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMember : LiveModel, IPlanetModel, ISharedPlanetMember
{
    #region IPlanetModel implementation

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(bool refresh = false) =>
        IPlanetModel.GetPlanetAsync(this, refresh);

    public override string BaseRoute =>
            $"api/members";

    #endregion

    /// <summary>
    /// Runs if a role is added, removed, or updated
    /// </summary>
    public event Func<MemberRoleEvent, Task> OnRoleModified;

    /// <summary>
    /// Cached roles
    /// </summary>
    private List<PlanetRole> _roles;

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
        var member = (await node.GetJsonAsync<PlanetMember>($"api/members/{id}")).Data;

        if (member is not null)
            await member.AddToCache();

        return member;
    }
    
    public override async Task OnUpdate(ModelUpdateEvent eventData)
    {
        var planet = await GetPlanetAsync();
        await planet.NotifyMemberUpdateAsync(this, eventData);
    }

    public override async Task OnDelete()
    {
        var planet = await GetPlanetAsync();
        await planet.NotifyMemberDeleteAsync(this);
    }

    public override async Task AddToCache<T>(T item, bool skipEvent = false)
    {
        await ValourCache.Put(Id, this, skipEvent);
        await ValourCache.Put((PlanetId, UserId), this, true); // Skip event because we already called it above
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
        var member = (await node.GetJsonAsync<PlanetMember>($"api/members/byuser/{planetId}/{userId}")).Data;

        if (member is not null)
        {
            await ValourCache.Put(member.Id, member);
            await ValourCache.Put((planetId, userId), member);
        }

        return member;
    }

    public async Task NotifyRoleEventAsync(MemberRoleEvent eventData)
    {
        // If roles aren't even loaded, the update will be there
        // when we actually bother to grab them. Do nothing.
        if (_roles is null)
            return;

        var role = eventData.Role;
        var existing = _roles.FirstOrDefault(x => x.Id == role.Id);

        switch (eventData.Type)
        {
            case MemberRoleEventType.Added:
            {
                // No need to add if it already exists
                if (existing is not null)
                    return;
                
                // Add and sort
                _roles.Add(role);
                _roles.Sort((a, b) => a.Position.CompareTo(b.Position));

                break;
            }
            case MemberRoleEventType.Removed:
            {
                // No need to remove if it doesn't exist
                if (existing is null)
                    return;
                
                // Remove (sort not needed for removing)
                _roles.Remove(existing);

                break;
            }
            case MemberRoleEventType.Updated:
            {
                // If it's updated and we don't have it, return.
                // This should not happen.
                if (existing is null)
                    return;

                break;
            }
        }

        if (OnRoleModified is not null)
            await OnRoleModified.Invoke(eventData);
    }
    
    
    /// <summary>
    /// Returns the primary role of this member
    /// </summary>
    public async ValueTask<PlanetRole> GetPrimaryRoleAsync(bool force_refresh = false)
    {
        if (_roles is null || force_refresh)
        {
            await LoadRolesAsync();
        }

        if (_roles.Count > 0)
            return _roles[0];

        return null;
    }

    /// <summary>
    /// Returns the role that is visually displayed for this member
    /// </summary>
    public async ValueTask<PlanetRole> GetDisplayedRoleAsync(bool force_refresh = false)
    {
        if (_roles is null || force_refresh)
        {
            await LoadRolesAsync();
        }

        // Null if no roles
        if ((_roles?.Count ?? 0) == 0)
            return null;
        
        // Return first role that has the display role, or the last (default)
        return _roles.FirstOrDefault(x => x.HasPermission(PlanetPermissions.DisplayRole)) 
               ?? _roles.LastOrDefault();
    }

    /// <summary>
    /// Returns the roles of this member
    /// </summary>
    public async ValueTask<List<PlanetRole>> GetRolesAsync(bool force_refresh = false)
    {
        if (_roles is null || force_refresh)
        {
            await LoadRolesAsync();
        }

        return _roles;
    }

    /// <summary>
    /// Returns if the member has the given role
    /// </summary>
    public async ValueTask<bool> HasRoleAsync(long id, bool force_refresh = false)
    {
        if (_roles is null || force_refresh)
        {
            await LoadRolesAsync();
        }

        return _roles.Any(x => x.Id == id);
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
    /// Returns if the member has the given permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetChannel channel, Permission permission) =>
        await channel.HasPermissionAsync(this, permission);

    public async Task<bool> HasPermissionAsync(PlanetPermission permission)
    {
        var planet = await GetPlanetAsync();
        if (planet.OwnerId == UserId)
            return true;

        var topRole = await GetPrimaryRoleAsync();
        return topRole.HasPermission(permission);
    }

    /// <summary>
    /// Loads all role Ids from the server
    /// </summary>
    public async Task LoadRolesAsync(List<long> roleIds = null)
    {
        if (roleIds is null)
            roleIds = (await Node.GetJsonAsync<List<long>>($"{IdRoute}/roles")).Data;

        if (_roles is null)
            _roles = new List<PlanetRole>();
        else
            _roles.Clear();

        foreach (var id in roleIds)
        {
            var role = await PlanetRole.FindAsync(id, PlanetId);

            if (role is not null)
                _roles.Add(role);
        }

        _roles.Sort((a, b) => a.Position.CompareTo(b.Position));
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
        (await GetPrimaryRoleAsync(force_refresh))?.Color ?? "#ffffff";


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

