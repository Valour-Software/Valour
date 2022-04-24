using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;

namespace Valour.Database.Items;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public abstract class Item : ISharedItem
{

    public const string Pref = "https://";
    public const string Post = ".nodes.valour.gg";

    public ulong Id { get; set; }

    [NotMapped]
    public abstract ItemType ItemType { get; }

    /// <summary>
    /// Returns the item with the given id
    /// </summary>
    public static async ValueTask<T> FindAsync<T>(ulong id, ValourDB db) 
        where T : Item =>
        await db.FindAsync<T>(id);

    /// <summary>
    /// Returns all of the given type within the database
    /// </summary>
    public static async Task<List<T>> FindAllAsync<T>(ValourDB db)
        where T : Item =>
        await db.Set<T>().ToListAsync();
}

