using System.ComponentModel.DataAnnotations;
using Valour.Server.API;
using Valour.Server.Nodes;
using Valour.Shared.Items;

namespace Valour.Server.Database.Items;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public abstract class Item : ISharedItem
{

    public const string UriPrefix = "https://";
    public const string UriPostfix = ".nodes.valour.gg";

    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// This is the node that returned the API item.
    /// This node should be used for any API 
    /// </summary>
    [NotMapped]
    [JsonInclude]
    public string NodeName => NodeAPI.Node.Name;

    /// <summary>
    /// Returns the item with the given id
    /// </summary>
    public static async ValueTask<T> FindAsync<T>(object id, ValourDB db)
        where T : Item =>
        await db.FindAsync<T>(id);

    /// <summary>
    /// Returns all of the given type within the database
    /// </summary>
    public static async Task<List<T>> FindAllAsync<T>(ValourDB db)
        where T : Item =>
        await db.Set<T>().ToListAsync();

    [JsonIgnore]
    public virtual string IdRoute =>
        $"{BaseRoute}/{{Id}}";

    [JsonIgnore]
    public virtual string BaseRoute =>
        $"api/{GetType().Name}";

    /// <summary>
    /// Returns the uri to a specific resource
    /// </summary>
    public virtual string GetUri() =>
        $"{NodeAPI.Node.Address}/{IdRoute}";
    //  https://coca.nodes.valour.gg/api/planetmember/001
}

