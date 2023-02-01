using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Models;

namespace Valour.Database;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

[Table("permissions_nodes")]
public class PermissionsNode : Item, ISharedPermissionsNode
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }

    [ForeignKey("RoleId")]
    public virtual PlanetRole Role { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Column("planet_id")]
    public long PlanetId { get; set; }

    /// <summary>
    /// The permission code that this node has set
    /// </summary>
    [Column("code")]
    public long Code { get; set; }

    /// <summary>
    /// A mask used to determine if code bits are disabled
    /// </summary>
    [Column("mask")]
    public long Mask { get; set; }

    /// <summary>
    /// The role this permissions node belongs to
    /// </summary>
    [Column("role_id")]
    public long RoleId { get; set; }

    /// <summary>
    /// The id of the object this node applies to
    /// If this is null, it is a base permission
    /// </summary>
    [Column("target_id")]
    public long? TargetId { get; set; }

    /// <summary>
    /// The type of object this node applies to
    /// </summary>
    [Column("target_type")]
    public PermissionsTargetType TargetType { get; set; }
}

