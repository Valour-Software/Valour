using System.Collections.Concurrent;
using Valour.Api.Authorization.Roles;
using Valour.Api.Planets;
using Valour.Api.Users;
using Valour.Client.Categories;

namespace Valour.Api.Client;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public static class ValourCache
{
    /// <summary>
    /// The high level cache object which contains the lower level caches
    /// </summary>
    public static Dictionary<Type, ConcurrentDictionary<object, object>> HCache = new();

    /// <summary>
    /// Places an item into the cache
    /// </summary>
    public static void Put<T>(object id, T obj) where T : class
    {
        if (obj == null)
            return;

        var type = typeof(T);

        if (!HCache.ContainsKey(type))
            HCache.Add(type, new ConcurrentDictionary<object, object>());

        HCache[type][id] = obj;
    }

    /// <summary>
    /// Returns true if the cache contains the item
    /// </summary>
    public static bool Contains<T>(object id) where T : class
    {
        var type = typeof(T);

        if (!HCache.ContainsKey(typeof(T)))
            return false;

        return HCache[type].ContainsKey(id);
    }

    /// <summary>
    /// Returns the item for the given id, or null if it does not exist
    /// </summary>
    public static T Get<T>(object id) where T : class
    {
        var type = typeof(T);

        if (HCache.ContainsKey(type))
            if (HCache[type].ContainsKey(id)) {
                var obj = HCache[type][id];
                if (obj is T)
                    return (T)obj;

                return null;
            }

        return null;
    }

    /// <summary>
    /// Removes an item if present in the cache
    /// </summary>
    public static void Remove<T>(object id) where T : class
    {
        var type = typeof(T);

        if (HCache.ContainsKey(type))
            if (HCache[type].ContainsKey(id))
            {
                HCache[type].Remove(id, out _);
            }
    }
}

