using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public struct PlanetUserKey : IEquatable<PlanetUserKey>
{
    public readonly long UserId;
    public readonly long PlanetId;
    
    public PlanetUserKey(long userId, long planetId)
    {
        UserId = userId;
        PlanetId = planetId;
    }
    
    public bool Equals(PlanetUserKey other) =>
        UserId == other.UserId && PlanetId == other.PlanetId;
}

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2024 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMember : ClientPlanetModel<PlanetMember, long>, ISharedPlanetMember
{
    // Extra cache for userid-planetid
    public static readonly Dictionary<PlanetUserKey, long> MemberIdLookup = new();

    public override string BaseRoute =>
        ISharedPlanetMember.BaseRoute;
    
    /// <summary>
    /// The id of the planet this belongs to
    /// </summary>
    public long PlanetId { get; set; }

    public override long? GetPlanetId()
        => PlanetId;

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
    public string MemberAvatar { get; set; }
    
    protected override void OnUpdated(ModelUpdateEvent<PlanetMember> eventData)
    {
        Planet?.NotifyMemberUpdate(eventData);
    }

    protected override void OnDeleted()
    {
        Planet?.NotifyMemberDelete(this);
    }
    
    public override PlanetMember AddToCacheOrReturnExisting()
    {
        var key = new PlanetUserKey(UserId, PlanetId);
        MemberIdLookup[key] = Id;
        
        return base.AddToCacheOrReturnExisting();
    }

    public override PlanetMember TakeAndRemoveFromCache()
    {
        var key = new PlanetUserKey(UserId, PlanetId);
        MemberIdLookup.Remove(key);
        
        return base.TakeAndRemoveFromCache();
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
    public async ValueTask<PlanetRole> GetPrimaryRoleAsync(bool refresh = false)
    {
        if (_roles is null || refresh)
        {
            await LoadRolesAsync();
        }

        if (_roles is null || _roles.Count == 0)
        {
            return null;
        }
        
        return _roles[0];
    }

    /// <summary>
    /// Returns the role that is visually displayed for this member
    /// </summary>
    public async ValueTask<PlanetRole> GetDisplayedRoleAsync(bool refresh = false)
    {
        if (_roles is null || refresh)
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
    public async ValueTask<List<PlanetRole>> GetRolesAsync(bool refresh = false)
    {
        if (_roles is null || refresh)
        {
            await LoadRolesAsync();
        }

        return _roles;
    }

    /// <summary>
    /// Returns if the member has the given role
    /// </summary>
    public async ValueTask<bool> HasRoleAsync(long id, bool refresh = false)
    {
        if (_roles is null || refresh)
        {
            await LoadRolesAsync();
        }
        
        if (_roles is null || _roles.Count == 0)
        {
            return false;
        }

        return _roles.Any(x => x.Id == id);
    }

    /// <summary>
    /// Returns if the member has the given role
    /// </summary>
    public ValueTask<bool> HasRoleAsync(PlanetRole role, bool refresh = false) =>
        HasRoleAsync(role.Id, refresh);

    /// <summary>
    /// Returns the authority of the member
    /// </summary>
    public async Task<int> GetAuthorityAsync() =>
        (await Node.GetJsonAsync<int>($"{IdRoute}/authority")).Data;

    /// <summary>
    /// Returns if the member has the given permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(Channel channel, Permission permission) =>
        await channel.HasPermissionAsync(UserId, permission);

    public async Task<bool> HasPermissionAsync(PlanetPermission permission)
    {
        if (Planet is null)
            return false;
        
        if (Planet.OwnerId == UserId)
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
    // TODO: this is weird, going to improve this
    public async Task SetLocalRoleIds(List<long> ids) =>
        await LoadRolesAsync(ids);

    /// <summary>
    /// Returns the user of the member
    /// </summary>
    public ValueTask<User> GetUserAsync(bool refresh = false) =>
        User.FindAsync(UserId, refresh);

    /// <summary>
    /// Returns the status of the member
    /// </summary>
    public async Task<string> GetStatusAsync(bool refresh = false) =>
        (await GetUserAsync(refresh))?.Status ?? "";


    /// <summary>
    /// Returns the role color of the member
    /// </summary>
    public async Task<string> GetRoleColorAsync(bool refresh = false) =>
        (await GetPrimaryRoleAsync(refresh))?.Color ?? "#ffffff";


    /// <summary>
    /// Returns the pfp url of the member
    /// </summary>
    public async ValueTask<string> GetAvatarUrlAsync(bool refresh = false)
    {
        if (!string.IsNullOrWhiteSpace(MemberAvatar))
            return MemberAvatar;

        return (await GetUserAsync(refresh)).GetAvatarUrl();
    }

    /// <summary>
    /// Returns the name of the member
    /// </summary>
    public async ValueTask<string> GetNameAsync(bool refresh = false)
    {
        if (!string.IsNullOrWhiteSpace(Nickname))
            return Nickname;

        return (await GetUserAsync(refresh))?.Name ?? "User not found";
    }
}

