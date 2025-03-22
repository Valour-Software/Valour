using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Valour.Sdk.ModelLogic;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

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
    [IgnoreRealtimeChanges]
    public User User { get; set; }
    
    private ImmutableList<PlanetRole>? _roles = null;
    
    [JsonIgnore]
    public ImmutableList<PlanetRole> Roles
    {
        get
        {
            _roles ??= Planet.GetRolesFromMembership(RoleMembership);
            return _roles;
        }
    }
    
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
    
    /// <summary>
    /// Flags representing the roles of the member
    /// </summary>
    public PlanetRoleMembership RoleMembership { get; set; }
    
    protected override void OnUpdated(ModelUpdatedEvent<PlanetMember> eventData)
    {
        if (eventData.Changes.On(x => x.RoleMembership))
        {
            // Clear cached roles
            _roles = null;
        }
    }

    protected override void OnDeleted()
    {
    }
    
    public Task<TaskResult> AddRoleAsync(long roleId) =>
        Planet.AddMemberRoleAsync(Id, roleId);
    
    public Task<TaskResult> RemoveRoleAsync(long roleId) =>
        Planet.RemoveMemberRoleAsync(Id, roleId);
    
    public override PlanetMember AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        var key = new PlanetMemberKey(UserId, PlanetId);
        Client.Cache.MemberKeyToId[key] = Id;

        return Planet.Members.Put(this, flags);
    }

    public override PlanetMember RemoveFromCache(bool skipEvents = false)
    {
        var key = new PlanetMemberKey(UserId, PlanetId);
        Client.Cache.MemberKeyToId.Remove(key);

        return Planet.Members.Remove(this, skipEvents);
    }

    public override void SyncSubModels(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        User = User.Sync(Client, flags);
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

