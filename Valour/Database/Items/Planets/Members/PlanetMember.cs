using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Database.Items.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets.Members;

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
public class PlanetMember : PlanetMemberBase
{

    // Relational DB stuff
    [ForeignKey("User_Id")]
    [JsonIgnore]
    public virtual User User { get; set; }

    [ForeignKey("Planet_Id")]
    [JsonIgnore]
    public virtual Planet Planet { get; set; }

    [InverseProperty("Member")]
    [JsonIgnore]
    public virtual ICollection<PlanetRoleMember> RoleMembership { get; set; }

    public static async Task<PlanetMember> FindAsync(ulong user_id, ulong planet_id, ValourDB db)
    {
        return await db.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == planet_id &&
                                                                  x.User_Id == user_id);
    }

    public static async Task<PlanetMember> FindAsync(ulong member_id, ValourDB db)
    {
        return await db.PlanetMembers.FindAsync(member_id);
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
}

