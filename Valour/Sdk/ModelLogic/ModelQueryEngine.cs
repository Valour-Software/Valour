using Valour.Sdk.Nodes;

namespace Valour.Sdk.ModelLogic;

/// <summary>
/// The model query engine allows for efficient querying of models
/// with automatic global caching and a local sliding cache.
/// </summary>
public class ModelQueryEngine<TModel>
    where TModel : ClientModel<TModel>
{
    private readonly string _route;
    private readonly Node _node;
    private readonly int _cacheSize;
    private int? _totalCount; // Updated when fetching missing data, null if unknown

    // Fixed-size array cache
    private readonly TModel[] _slidingCache;
    private int _cacheStartIndex; // The index in the dataset corresponding to _cache[0]

    public ModelQueryEngine(Node node, string route, int cacheSize = 200)
    {
        _route = route;
        _node = node;
        _cacheSize = cacheSize;
        _slidingCache = new TModel[_cacheSize];
        _cacheStartIndex = 0;
    }

    public async Task<ModelQueryResponse<TModel>> GetItemsAsync(int skip, int take)
    {
        var items = new List<TModel>(take);

        // Check if requested range is within the current cache window
        if (IsWithinCache(skip, take))
        {
            // Retrieve items from cache
            var offset = skip - _cacheStartIndex;
            for (var i = 0; i < take; i++)
            {
                items.Add(_slidingCache[offset + i]);
            }
        }
        else
        {
            // Adjust the cache window to include the requested range
            await AdjustCacheWindow(skip, take);

            // After adjusting, retrieve items from cache
            var offset = skip - _cacheStartIndex;
            for (var i = 0; i < take; i++)
            {
                items.Add(_slidingCache[offset + i]);
            }
        }

        return new ModelQueryResponse<TModel>
        {
            Items = items,
            TotalCount = _totalCount ?? items.Count
        };
    }

    private bool IsWithinCache(int skip, int take)
    {
        return skip >= _cacheStartIndex && (skip + take) <= (_cacheStartIndex + _cacheSize);
    }

    private async Task AdjustCacheWindow(int skip, int take)
    {
        int newStartIndex;
        if (take >= _cacheSize)
        {
            // The requested range is larger than or equal to the cache size
            newStartIndex = skip;
        }
        else if (skip < _cacheStartIndex)
        {
            // Scrolling backward
            newStartIndex = Math.Max(skip - (_cacheSize - take), 0);
        }
        else
        {
            // Scrolling forward
            newStartIndex = skip;
        }

        // Calculate how much to shift existing data
        int shift = _cacheStartIndex - newStartIndex;
        if (shift != 0)
        {
            ShiftCache(shift);
        }

        _cacheStartIndex = newStartIndex;

        // Fetch missing data
        await FetchMissingData();
    }

    private void ShiftCache(int shift)
    {
        if (shift > 0)
        {
            // Scrolling backward: Shift right
            for (var i = _cacheSize - 1; i >= shift; i--)
            {
                _slidingCache[i] = _slidingCache[i - shift];
            }

            // Clear the vacated positions
            for (var i = 0; i < shift; i++)
            {
                _slidingCache[i] = default;
            }
        }
        else if (shift < 0)
        {
            // Scrolling forward: Shift left
            shift = -shift;
            for (var i = 0; i < _cacheSize - shift; i++)
            {
                _slidingCache[i] = _slidingCache[i + shift];
            }

            // Clear the vacated positions
            for (var i = _cacheSize - shift; i < _cacheSize; i++)
            {
                _slidingCache[i] = default;
            }
        }
    }

    // Fetches missing data and places it into the cache
    // Returns false if there was an error
    private async Task<bool> FetchMissingData()
    {
        var min = int.MaxValue;
        var max = -1; // -1 can't happen naturally, so we can use it to see if empty
        
        for (var i = 0; i < _cacheSize; i++)
        {
            if (_slidingCache[i] == null)
            {
                var index = _cacheStartIndex + i;
                if (index < min)
                    min = index;
                
                if (index > max)
                    max = index;
            }
        }
        
        // Nothing to fetch
        if (max == -1)
            return true;

        
        var skip = min;
        var take = max - skip + 1;

        var result = await _node.GetJsonAsync<ModelQueryResponse<TModel>>($"{_route}?skip={skip}&take={take}");

        if (!result.Success || result.Data.Items is null)
        {
            // There will probably be a gap in the cache, but we can't do anything about it
            // We'll return false so it can be handled.
            return false;
        }

        // Update total count
        _totalCount = result.Data.TotalCount;

        // Sync to client cache and update references in list
        result.Data.Sync(_node.Client);

        // Place fetched items into local sliding cache
        for (var i = 0; i < result.Data.Items.Count; i++)
        {
            var index = skip + i;
            var cachePosition = index - _cacheStartIndex;
            if (cachePosition >= 0 && cachePosition < _cacheSize)
            {
                _slidingCache[cachePosition] = result.Data.Items[i];
            }
        }
     
        return true;
    }
}