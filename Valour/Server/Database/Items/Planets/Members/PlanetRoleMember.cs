using Valour.Server.Database.Items.Users;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Members;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Planets.Members;

[Table("planet_role_members")]
public class PlanetRoleMember : Item, IPlanetItem, ISharedPlanetRoleMember
{
    #region IPlanetItem Implementation

    [JsonIgnore]
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }

    public long PlanetId { get; set; }

    public ValueTask<Planet> GetPlanetAsync(ValourDB db) =>
        IPlanetItem.GetPlanetAsync(this, db);

    [JsonIgnore]
    public override string BaseRoute =>
        $"/api/planet/{{planetId}}/{nameof(PlanetRoleMember)}";

    #endregion

    [ForeignKey("MemberId")]
    [JsonIgnore]
    public virtual PlanetMember Member { get; set; }

    [ForeignKey("RoleId")]
    [JsonIgnore]
    public virtual PlanetRole Role { get; set; }

    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("role_id")]
    public long RoleId { get; set; }

    [Column("member_id")]
    public long MemberId { get; set; }

    // This doesn't really even need API routes, it's just used internally to map roles to members.
    // Use a route from PlanetMember if you need to get someone's roles.
}

