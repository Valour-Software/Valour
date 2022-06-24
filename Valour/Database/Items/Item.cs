﻿using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Database.Nodes;

namespace Valour.Database.Items;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public abstract class Item : ISharedItem
{

    public const string UriPrefix = "https://";
    public const string UriPostfix = ".nodes.valour.gg";

    public ulong Id { get; set; }

    [NotMapped]
    public abstract ItemType ItemType { get; }

    /// <summary>
    /// This is the node that returned the API item.
    /// This node should be used for any API 
    /// </summary>
    [NotMapped]
    [JsonInclude]
    public string Node => DeployedNode.Instance.Name;

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

    public virtual string IdRoute =>
        $"{ItemType}/{{id}}";

    public virtual string BaseRoute =>
        $"{ItemType}";

    /// <summary>
    /// Returns the uri to a specific resource
    /// </summary>
    public virtual string GetUri() =>
        $"{DeployedNode.Instance.Address}/{IdRoute}";
      //  https://coca.nodes.valour.gg/planetmember/001
}

