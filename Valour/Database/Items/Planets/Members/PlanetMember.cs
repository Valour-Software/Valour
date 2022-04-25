using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Database.Items.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets.Members;
using Valour.Shared;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets.Channels;
using Valour.Shared.Items;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Members;

/// <summary>
/// This class exists to add server funtionality to the PlanetMember
/// class.
/// </summary>
public class PlanetMember : PlanetItem, ISharedPlanetMember, INodeSpecific
{

    public const int FLAG_UPDATE_ROLES = 0x01;

    // Relational DB stuff
    [ForeignKey("User_Id")]
    [JsonIgnore]
    public virtual User User { get; set; }

    [InverseProperty("Member")]
    [JsonIgnore]
    public virtual ICollection<PlanetRoleMember> RoleMembership { get; set; }

    /// <summary>
    /// The user within the planet
    /// </summary>
    public ulong User_Id { get; set; }

    /// <summary>
    /// The name to be used within the planet
    /// </summary>
    public string Nickname { get; set; }

    /// <summary>
    /// The pfp to be used within the planet
    /// </summary>
    public string Member_Pfp { get; set; }

    public override ItemType ItemType => ItemType.PlanetMember;

    public static async Task<PlanetMember> FindAsync(ulong user_id, ulong planet_id, ValourDB db)
    {
        return await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == planet_id &&
                                                                  x.User_Id == user_id);
    }

    /// <summary>
    /// Returns all of the roles for a planet user
    /// </summary>
    public async Task<List<PlanetRole>> GetRolesAsync(ValourDB db = null)
    {
        List<PlanetRole> roles;

        if (RoleMembership == null)
        {
            await LoadRoleMembershipAsync(db);
        }

        roles = RoleMembership.Select(x => x.Role).ToList();

        return roles;
    }

    /// <summary>
    /// Loads role membership data from database
    /// </summary>
    public async Task LoadRoleMembershipAsync(ValourDB db = null)
    {
        bool createdb = false;
        if (db == null)
        {
            db = new ValourDB(ValourDB.DBOptions);
            createdb = true;
        }

        await db.Attach(this).Collection(x => x.RoleMembership)
                                 .Query()
                                 .Include(x => x.Role)
                                 .OrderBy(x => x.Role.Position)
                                 .LoadAsync();

        if (createdb)
        {
            await db.DisposeAsync();
        }
    }

    /// <summary>
    /// Returns the member's primary role
    /// </summary>
    public async Task<PlanetRole> GetPrimaryRoleAsync(ValourDB db = null)
    {
        if (RoleMembership == null)
        {
            await LoadRoleMembershipAsync(db);
        }

        return RoleMembership.FirstOrDefault().Role;
    }

    /// <summary>
    /// Returns if the member has the given permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetPermission permission, ValourDB db)
    {
        Planet ??= await db.Planets.FindAsync(Planet_Id);
        return await Planet.HasPermissionAsync(this, permission, db);
    }

    /// <summary>
    /// Returns a success or failure for the combination of permissions
    /// Send in pairs of permissions with their target object! For example (ChannelPermission, Channel) or (UserPermission, AuthToken)
    /// 
    /// Todo: Use interface to make this better? IPermissable?
    /// </summary>
    public async Task<TaskResult> HasAllPermissions(ValourDB db, params (Permission perm, object target)[] permission_pairs)
    {
        foreach (var pair in permission_pairs)
        {
            if (pair.perm is UserPermission)
            {
                var uperm = pair.perm as UserPermission;
                var token = pair.target as AuthToken;

                if (!token.HasScope(uperm))
                    return new TaskResult(false, "Token lacks " + uperm.Name + " permission.");
            }
            else if (pair.perm is PlanetPermission)
            {
                var pperm = pair.perm as PlanetPermission;
                var planet = pair.target as Planet;

                if (!await planet.HasPermissionAsync(this, pperm, db))
                    return new TaskResult(false, "Member lacks " + pperm.Name + " planet permission.");
            }
            else if (pair.perm is ChatChannelPermission)
            {
                var cperm = pair.perm as ChatChannelPermission;
                var channel = pair.target as PlanetChatChannel;

                if (!await channel.HasPermission(this, cperm, db))
                    return new TaskResult(false, "Member lacks " + cperm.Name + " channel permission.");
            }
            else if (pair.perm is CategoryPermission)
            {
                var cperm = pair.perm as CategoryPermission;
                var channel = pair.target as PlanetCategoryChannel;

                if (!await channel.HasPermission(this, cperm, db))
                    return new TaskResult(false, "Member lacks " + cperm.Name + " category permission.");
            }
            else
            {
                throw new Exception("This type of permission needs to be implemented!");
            }
        }

        return new TaskResult(true, "Authorized.");
    }

    /// <summary>
    /// Returns the user (async)
    /// </summary>
    public async Task<User> GetUserAsync()
    {
        using (ValourDB db = new ValourDB(ValourDB.DBOptions))
        {
            return await db.Users.FindAsync(User_Id);
        }
    }

    /// <summary>
    /// Returns the planet (async)
    /// </summary>
    public async Task<Planet> GetPlanetAsync()
    {
        if (Planet != null) return Planet;

        using (ValourDB db = new ValourDB(ValourDB.DBOptions))
        {
            Planet = await db.Planets.FindAsync(Planet_Id);
        }

        return Planet;
    }

    public async Task<ulong> GetAuthorityAsync()
    {
        if (Planet == null)
        {
            Planet = await GetPlanetAsync();
        }

        if (Planet.Owner_Id == User_Id)
        {
            // Highest possible authority for owner
            return ulong.MaxValue;
        }
        else
        {
            var primaryRole = await GetPrimaryRoleAsync();

            return primaryRole.GetAuthority();
        }
    }

    public async Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db)
    {
        // Needs to be able to GET in order to do anything else
        var canGet = await ((IPlanetItemAPI<PlanetMember>)this).CanGetAsync(member, db);
        if (!canGet.Success)
            return canGet;

        Planet ??= await GetPlanetAsync();

        // Can always remove self
        if (Id != member.Id)
        {
            // Need kick or ban to remove anyone else (ban implicitly grants kick)
            if (!(await Planet.HasPermissionAsync(member, PlanetPermissions.Kick, db) ||
                  await Planet.HasPermissionAsync(member, PlanetPermissions.Ban, db)))
            {
                return new TaskResult(false, "Member lacks planet permission " +
                    PlanetPermissions.Kick.Name + " or " + PlanetPermissions.Ban.Name);
            }
        }

        return new TaskResult(true, "Success");
    }

    public async Task<TaskResult> CanUpdateAsync(PlanetMember member, ValourDB db)
    {
        // Needs to be able to GET in order to do anything else
        var canGet = await ((IPlanetItemAPI<PlanetMember>)this).CanGetAsync(member, db);
        if (!canGet.Success)
            return canGet;

        // Only a member can edit themselves
        if (Id != member.Id)
        {
            return new TaskResult(false, "Cannot edit another member.");
        }

        return new TaskResult(true, "Success");
    }

    public async Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db)
    {
        // TODO: Be more specific
        return await Task.FromResult(
            new TaskResult(false, "Membership is created through the Planet API")
        );
    }

    public async Task<TaskResult> ValidateItemAsync(PlanetMember old, ValourDB db)
    {
        // This means this is an update rather
        // than a creation
        if (old is not null)
        {
            // Cannot change user ID
            if (old.User_Id != User_Id)
            {
                return new TaskResult(false, "User_Id cannot be changed.");
            }
        }

        // Ensure nickname is valid
        if (Nickname.Length > 32)
        {
            return new TaskResult(false, "Maximum nickname is 32 characters.");
        }

        if (Member_Pfp != old.Member_Pfp)
        {
            // TODO: Automatically use VMPS
            return new TaskResult(false, "Profile picture must be changed through Upload API.");
        }

        return await Task.FromResult(
            new TaskResult(true, "Success")
        );
    }
}

