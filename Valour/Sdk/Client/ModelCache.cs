using System.Collections.Concurrent;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Services;

namespace Valour.Sdk.Client;

/*  Valour (TM) - A free and secure chat client
*  Copyright (C) 2024 Valour Software LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class ModelCache<TModel, TId> 
    where TModel : ClientModel<TModel, TId>
    where TId : IEquatable<TId>
{
    private readonly ConcurrentDictionary<TId, TModel> _innerCache = new();
    
    /// <summary>
    /// Places an item into the cache. Returns if the item already exists and should be updated.
    /// </summary>
    public TModel Put(TId id, TModel model)
    {
        // Empty object is ignored
        if (model is null)
            return null;

        if (!_innerCache.TryAdd(id, model)) // Fails if already exists
        {
            return _innerCache[id];
        }

        return null;
    }
    
    /// <summary>
    /// Places an item into the cache, and replaces the item if it already exists
    /// </summary>
    public void PutReplace(TId id, TModel model)
    {
        _innerCache[id] = model;
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
    public bool TryGet(TId id, out TModel model)
    {
        return _innerCache.TryGetValue(id, out model);
    }

    /// <summary>
    /// Removes an item if present in the cache
    /// </summary>
    public void Remove(TId id)
    {
        _innerCache.TryRemove(id, out var _);
    }
    
    public TModel TakeAndRemove(TId id)
    {
        _innerCache.TryRemove(id, out var model);
        return model;
    }
}

