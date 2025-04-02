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
    
    // Track the range of populated data in the dataset
    private int _populatedRangeStart; // First populated index in the dataset
    private int _populatedRangeEnd;   // One past the last populated index in the dataset

    public ModelQueryEngine(Node node, string route, int cacheSize = 200)
    {
        _route = route;
        _node = node;
        _cacheSize = cacheSize;
        _slidingCache = new TModel[_cacheSize];
        _cacheStartIndex = 0;
        _populatedRangeStart = 0;
        _populatedRangeEnd = 0; // Initially no data is populated (start == end means empty)
    }

    public async Task<ModelQueryResponse<TModel>> GetItemsAsync(int skip, int take)
    {
        var items = new List<TModel>(take);

        // Check if requested range is fully within the populated area
        if (IsRangePopulated(skip, take))
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
            bool fetchSuccess = await AdjustCacheWindow(skip, take);
            
            if (fetchSuccess)
            {
                // After adjusting, retrieve items from cache
                var offset = skip - _cacheStartIndex;
                for (var i = 0; i < take; i++)
                {
                    // Only add non-null items (might happen at edges of populated range)
                    if (_slidingCache[offset + i] != null)
                    {
                        items.Add(_slidingCache[offset + i]);
                    }
                }
            }
            else
            {
                // If fetching failed, try to directly fetch the requested range
                var result = await _node.GetJsonAsync<ModelQueryResponse<TModel>>(
                    $"{_route}?skip={skip}&take={take}");
                    
                if (result.Success && result.Data.Items != null)
                {
                    result.Data.Sync(_node.Client);
                    items.AddRange(result.Data.Items);
                    _totalCount = result.Data.TotalCount;
                }
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
    
    private bool IsRangePopulated(int skip, int take)
    {
        // Check if the entire requested range is within the populated range
        return skip >= _populatedRangeStart && 
               (skip + take) <= _populatedRangeEnd &&
               IsWithinCache(skip, take);
    }

    private async Task<bool> AdjustCacheWindow(int skip, int take)
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
            newStartIndex = Math.Max(skip - (_cacheSize - take) / 2, 0);
        }
        else
        {
            // Scrolling forward
            newStartIndex = Math.Max(skip - (_cacheSize - take) / 2, 0);
        }

        // Calculate how much to shift existing data
        int shift = _cacheStartIndex - newStartIndex;
        
        // Update populated range to reflect the shift
        if (_populatedRangeEnd > _populatedRangeStart) // if we have populated data
        {
            _populatedRangeStart -= shift;
            _populatedRangeEnd -= shift;
            
            // Clamp to valid range after shift
            _populatedRangeStart = Math.Max(_populatedRangeStart, newStartIndex);
            _populatedRangeEnd = Math.Min(_populatedRangeEnd, newStartIndex + _cacheSize);
            
            // Reset if invalid range
            if (_populatedRangeStart >= _populatedRangeEnd)
            {
                _populatedRangeStart = _populatedRangeEnd = newStartIndex;
            }
        }
        else
        {
            // No populated data yet
            _populatedRangeStart = _populatedRangeEnd = newStartIndex;
        }
        
        if (shift != 0)
        {
            ShiftCache(shift);
        }

        _cacheStartIndex = newStartIndex;

        // Fetch missing data
        return await FetchMissingData();
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
        // Calculate the range of data we need to fetch
        int fetchStart, fetchEnd;
        
        // If we have no populated data yet
        if (_populatedRangeStart >= _populatedRangeEnd)
        {
            fetchStart = _cacheStartIndex;
            fetchEnd = _cacheStartIndex + _cacheSize;
        }
        else
        {
            // We need to fetch data before the populated range
            if (_cacheStartIndex < _populatedRangeStart)
            {
                fetchStart = _cacheStartIndex;
                fetchEnd = _populatedRangeStart;
            }
            // We need to fetch data after the populated range
            else if (_cacheStartIndex + _cacheSize > _populatedRangeEnd)
            {
                fetchStart = _populatedRangeEnd;
                fetchEnd = _cacheStartIndex + _cacheSize;
            }
            else
            {
                // No need to fetch data
                return true;
            }
        }
        
        // Trim to valid range
        fetchStart = Math.Max(fetchStart, _cacheStartIndex);
        fetchEnd = Math.Min(fetchEnd, _cacheStartIndex + _cacheSize);
        
        if (fetchStart >= fetchEnd)
        {
            // Nothing to fetch
            return true;
        }
        
        var skip = fetchStart;
        var take = fetchEnd - fetchStart;

        var result = await _node.GetJsonAsync<ModelQueryResponse<TModel>>(
            $"{_route}?skip={skip}&take={take}");

        if (!result.Success || result.Data.Items is null)
        {
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
        
        // Update populated range
        int newPopulatedStart = Math.Min(_populatedRangeStart, skip);
        int newPopulatedEnd = Math.Max(_populatedRangeEnd, skip + result.Data.Items.Count);
        
        // If ranges are adjacent or overlapping, merge them
        if (newPopulatedStart <= _populatedRangeEnd && _populatedRangeStart <= newPopulatedEnd)
        {
            _populatedRangeStart = Math.Min(newPopulatedStart, _populatedRangeStart);
            _populatedRangeEnd = Math.Max(newPopulatedEnd, _populatedRangeEnd);
        }
        // Otherwise, just use the newly fetched range
        else if (result.Data.Items.Count > 0)
        {
            _populatedRangeStart = skip;
            _populatedRangeEnd = skip + result.Data.Items.Count;
        }
        
        // Clamp populated range to cache bounds
        _populatedRangeStart = Math.Max(_populatedRangeStart, _cacheStartIndex);
        _populatedRangeEnd = Math.Min(_populatedRangeEnd, _cacheStartIndex + _cacheSize);
     
        return true;
    }
}
