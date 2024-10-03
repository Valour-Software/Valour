using System.Collections.Concurrent;

namespace Valour.Sdk.Client;

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
    public static async Task Put<T>(object id, T obj, bool skipEvent = false, int flags = 0) where T : LiveModel
    {
        // Empty object is ignored
        if (obj == null)
            return;

        // Get the type of the item
        var type = typeof(T);

        // If there isn't a cache for this type, create one
        if (!HCache.ContainsKey(type))
            HCache.Add(type, new ConcurrentDictionary<object, object>());

        // If there is already an object with this ID, update it
        if (HCache[type].ContainsKey(id))
        {
            await ValourClient.UpdateItem(obj, flags, skipEvent);
        }
        else // Otherwise, place it into the cache
        {
            HCache[type][id] = obj;
        }
    }

    /// <summary>
    /// Returns true if the cache contains the item
    /// </summary>
    public static bool Contains<T>(object id) where T : LiveModel
    {
        var type = typeof(T);

        if (!HCache.ContainsKey(typeof(T)))
            return false;

        return HCache[type].ContainsKey(id);
    }

    /// <summary>
    /// Returns all the items of the given type. You can use Linq functions like .Where on this function.
    /// </summary>
    public static IEnumerable<T> GetAll<T>() where T : class
    {
        var type = typeof(T);

        if (!HCache.ContainsKey(type))
            yield break;

        foreach (T item in HCache[type].Values)
            yield return item;
    }


    /// <summary>
    /// Returns the item for the given id, or null if it does not exist
    /// </summary>
    public static T Get<T>(object id) where T : LiveModel
    {
        var type = typeof(T);

        if (HCache.ContainsKey(type))
            if (HCache[type].ContainsKey(id)) 
                return HCache[type][id] as T;

        return null;
    }

    /// <summary>
    /// Removes an item if present in the cache
    /// </summary>
    public static void Remove<T>(object id) where T : LiveModel
    {
        var type = typeof(T);

        if (HCache.ContainsKey(type))
            if (HCache[type].ContainsKey(id))
            {
                HCache[type].Remove(id, out _);
            }
    }
}

