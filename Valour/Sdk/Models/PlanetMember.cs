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

public class PlanetMember : ClientPlanetModel<PlanetMember, long>, ISharedPlanetMember, IMessageAuthor
{
    public override string BaseRoute =>
        ISharedPlanetMember.BaseRoute;
    
    /// <summary>
    /// The user of the member
    /// </summary>
    public User User { get; private set; }
    
    // User related properties //
    
    /// <summary>
    /// Returns the status of the member
    /// </summary>
    public string Status => User?.Status ?? "";
    
    /// <summary>
    /// Returns the name of the member
    /// </summary>
    public string Name => string.IsNullOrWhiteSpace(Nickname) ? (User?.Name ?? "User not found") : Nickname;
    
    /// <summary>
    /// The id of the planet this belongs to
    /// </summary>
    public long PlanetId { get; set; }

    protected override long? GetPlanetId()
        => PlanetId;

    /// <summary>
    /// The member's roles
    /// </summary>
    public readonly SortedModelList<PlanetRole, long> Roles = new();
    
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
        // Sync user first
        User = Client.Cache.Sync(User);
        
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
    public async Task<bool> HasPermission(Channel channel, Permission permission) =>
        await channel.HasPermissionAsync(UserId, permission);

    public bool HasPermission(PlanetPermission permission)
    {
        if (Planet is null)
            return false;
        
        if (Planet.OwnerId == UserId)
            return true;

        return PrimaryRole.HasPermission(permission);
    }

    /// <summary>
    /// Loads the member's role Ids from the server and loads associated roles into the Roles collection
    /// </summary>
    public async Task FetchRoleMembershipAsync(List<long> roleIds = null)
    {
        if (roleIds is null)
            roleIds = (await Node.GetJsonAsync<List<long>>($"{IdRoute}/roles")).Data;
        
        Roles.Clear(true);
        
        foreach (var id in roleIds)
        {
            var role = await Planet.FetchRoleAsync(id);
            if (role is not null)
            {
                Roles.UpsertNoSort(role);
            }
        }
        
        Roles.Sort();
        Roles.NotifySet();
    }

    /// <summary>
    /// Sets the role Ids manually. This exists for optimization purposes, and you probably shouldn't use it.
    /// It will NOT change anything on the server.
    /// </summary>
    // TODO: this is weird, going to improve this
    public async Task SetLocalRoleIds(List<long> ids) =>
        await FetchRoleMembershipAsync(ids);


    /// <summary>
    /// Returns the role color of the member
    /// </summary>
    public string GetRoleColor() =>
        PrimaryRole?.Color ?? "#ffffff";


    /// <summary>
    /// Returns the pfp url of the member
    /// </summary>
    public string GetAvatar(AvatarFormat format = AvatarFormat.Webp256)
    {
        if (!string.IsNullOrWhiteSpace(MemberAvatar)) // TODO: do same thing as user
            return MemberAvatar;

        return User?.GetAvatar(format) ?? ISharedUser.DefaultAvatar;
    }
    
    public string GetFailedAvatar() =>
        User?.GetFailedAvatar() ?? ISharedUser.DefaultAvatar;
}

