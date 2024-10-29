using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Models;

public struct PlanetMemberKey : IEquatable<PlanetMemberKey>
{
    private readonly long _userId;
    private readonly long _planetId;
    
    public PlanetMemberKey(long userId, long planetId)
    {
        _userId = userId;
        _planetId = planetId;
    }
    
    public bool Equals(PlanetMemberKey other) =>
        _userId == other._userId && _planetId == other._planetId;
}

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2024 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetMember : ClientPlanetModel<PlanetMember, long>, ISharedPlanetMember
{
    public override string BaseRoute =>
        ISharedPlanetMember.BaseRoute;
    
    /// <summary>
    /// The id of the planet this belongs to
    /// </summary>
    public long PlanetId { get; set; }

    protected override long? GetPlanetId()
        => PlanetId;

    /// <summary>
    /// The member's roles
    /// </summary>
    public readonly SortedReactiveModelStore<PlanetRole, long> Roles = new();
    
    /// <summary>
    /// The primary role of the member
    /// </summary>
    public PlanetRole PrimaryRole => Roles.FirstOrDefault();
    
    /// <summary>
    /// The authority of the member
    /// </summary>
    public uint Authority =>
        Planet.OwnerId == UserId ? uint.MaxValue : PrimaryRole?.GetAuthority() ?? 0;

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
        Planet?.OnMemberUpdated(eventData);
    }

    protected override void OnDeleted()
    {
        Planet?.OnMemberDeleted(this);
    }
    
    public override PlanetMember AddToCacheOrReturnExisting()
    {
        var key = new PlanetMemberKey(UserId, PlanetId);
        Client.Cache.MemberKeyToId[key] = Id;
        
        return Client.Cache.PlanetMembers.Put(Id, this);
    }

    public override PlanetMember TakeAndRemoveFromCache()
    {
        var key = new PlanetMemberKey(UserId, PlanetId);
        Client.Cache.MemberKeyToId.Remove(key);

        Client.Cache.PlanetMembers.Remove(Id);
        return this;
    }   

    public void OnRoleUpdated(ModelUpdateEvent<PlanetRole> eventData)
    {
        // If we have the role, update it
        Roles.Update(eventData);
    }

    public void OnRoleDeleted(PlanetRole role)
    {
        Roles.Remove(role);
    }
    
    public void OnRoleAdded(PlanetRole role)
    {
        Roles.Upsert(role);
    }
    
    public void OnRoleRemoved(PlanetRole role)
    {
        Roles.Remove(role);
    }

    /// <summary>
    /// Returns the role that is visually displayed for this member
    /// </summary>
    public PlanetRole GetDisplayedRoleAsync(bool refresh = false)
    {
        // Return first role that has the display role, or the last (default)
        return Roles.FirstOrDefault(x => x.HasPermission(PlanetPermissions.DisplayRole));
    }

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
    /// Loads all of the member's role Ids from the server
    /// </summary>
    public async Task LoadRolesAsync(List<long> roleIds = null)
    {
        if (roleIds is null)
            roleIds = (await Node.GetJsonAsync<List<long>>($"{IdRoute}/roles")).Data;
        
        _roles.Clear();

        foreach (var id in roleIds)
        {
            var role = await Planet.FetchRoleAsync(id);
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

