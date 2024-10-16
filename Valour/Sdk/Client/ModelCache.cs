using System.Collections.Concurrent;

namespace Valour.Sdk.Client;

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2024 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class ModelCache<TModel, TId> where TModel : ClientModel<TModel, TId>
{
    
    private readonly ConcurrentDictionary<TId, TModel> _innerCache = new();
    
    /// <summary>
    /// Places an item into the cache
    /// </summary>
    public Task Put(TId id, TModel model, bool skipEvent = false, int flags = 0)
    {
        // Empty object is ignored
        if (model == null)
            return Task.CompletedTask;

        if (!_innerCache.TryAdd(id, model)) // Fails if already exists
        {
            return ValourClient.UpdateItem(model, flags, skipEvent); // Update if already exists
        }
    }

    /// <summary>
    /// Returns true if the cache contains the item
    /// </summary>
    public bool Contains(TId id)
    {
        return _innerCache.ContainsKey(id);
    }

    /// <summary>
    /// Returns all the items of the given type. You can use Linq functions like .Where on this function.
    /// </summary>
    public IEnumerable<TModel> GetAll()
    {
        return _innerCache.Values;
    }


    /// <summary>
    /// Returns the item for the given id, or null if it does not exist
    /// </summary>
    public TModel Get(TId id)
    {
        return _innerCache.GetValueOrDefault(id);
    }

    /// <summary>
    /// Removes an item if present in the cache
    /// </summary>
    public void Remove(TId id)
    {
        _innerCache.TryRemove(id, out var _);
    }
}

