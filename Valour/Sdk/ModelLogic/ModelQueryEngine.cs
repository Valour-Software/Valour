using Valour.Sdk.Nodes;
using System.Collections.Generic;

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

    // Cache data
    private readonly TModel[] _cache;
    private int _cacheStartIndex = 0;
    
    // Set of ranges that we've already fetched (to avoid duplicate fetches)
    private readonly HashSet<(int Start, int End)> _fetchedRanges = new HashSet<(int Start, int End)>();
    
    // Last request information for prefetching
    private int _lastRequestedStart = -1;
    private int _lastRequestedEnd = -1;
    
    public ModelQueryEngine(Node node, string route, int cacheSize = 200)
    {
        _route = route;
        _node = node;
        _cacheSize = cacheSize;
        _cache = new TModel[_cacheSize];
    }

    /// <summary>
    /// Gets items from the specified range, using cache when possible and fetching from 
    /// the server when necessary.
    /// </summary>
    public async Task<ModelQueryResponse<TModel>> GetItemsAsync(int skip, int take)
    {
        // Basic validation
        take = Math.Max(0, Math.Min(take, 1000));
        if (take == 0)
        {
            return new ModelQueryResponse<TModel>
            {
                Items = new List<TModel>(),
                TotalCount = _totalCount ?? 0
            };
        }

        var items = new List<TModel>(take);
        
        // Detect scroll direction for prefetching
        int requestEnd = skip + take;
        bool scrollingDown = skip > _lastRequestedStart;
        bool scrollingUp = requestEnd < _lastRequestedEnd;
        _lastRequestedStart = skip;
        _lastRequestedEnd = requestEnd;
        
        // Always check if we need to adjust the cache window
        // This is critical for proper sliding behavior
        if (!IsRangeInCache(skip, take) || skip < _cacheStartIndex || skip + take > _cacheStartIndex + _cacheSize)
        {
            // Adjust the cache window - center it around the requested range
            AdjustCacheWindow(skip, take);
        }

        // Check what parts of the requested range are missing from cache
        var missingRanges = GetMissingRanges(skip, take);
        
        // Fetch any missing ranges
        if (missingRanges.Count > 0)
        {
            foreach (var range in missingRanges)
            {
                await FetchDataRange(range.Start, range.End - range.Start);
            }
        }
        
        // Retrieve items from cache
        for (int i = 0; i < take; i++)
        {
            int globalIndex = skip + i;
            int cacheIndex = globalIndex - _cacheStartIndex;
            
            if (cacheIndex >= 0 && cacheIndex < _cacheSize && _cache[cacheIndex] != null)
            {
                items.Add(_cache[cacheIndex]);
            }
        }
        
        // Prefetch in the scroll direction
        if (scrollingDown)
        {
            _ = Task.Run(async () =>
            {
                // Prefetch ahead
                int prefetchStart = skip + take;
                int prefetchSize = Math.Min(take, _cacheSize / 4); // Prefetch about 1/4 of cache size
                var prefetchMissing = GetMissingRanges(prefetchStart, prefetchSize);
                
                foreach (var range in prefetchMissing)
                {
                    await FetchDataRange(range.Start, range.End - range.Start);
                }
            });
        }
        else if (scrollingUp)
        {
            _ = Task.Run(async () =>
            {
                // Prefetch before
                int prefetchSize = Math.Min(take, _cacheSize / 4);
                int prefetchStart = Math.Max(0, skip - prefetchSize);
                var prefetchMissing = GetMissingRanges(prefetchStart, skip - prefetchStart);
                
                foreach (var range in prefetchMissing)
                {
                    await FetchDataRange(range.Start, range.End - range.Start);
                }
            });
        }
        
        return new ModelQueryResponse<TModel>
        {
            Items = items,
            TotalCount = _totalCount ?? items.Count
        };
    }

    /// <summary>
    /// Checks if a range is entirely within the cache window.
    /// </summary>
    private bool IsRangeInCache(int start, int count)
    {
        int end = start + count;
        return start >= _cacheStartIndex && end <= _cacheStartIndex + _cacheSize;
    }

    /// <summary>
    /// Adjusts the cache window to always include the requested range.
    /// </summary>
    private void AdjustCacheWindow(int start, int count)
    {
        // Calculate new cache window position to center around the requested range when possible
        int requestEnd = start + count;
        int requestCenter = start + count / 2;
        
        // Calculate how much we need to shift the window to include the requested range
        int newStartIndex;
        
        // If the requested range is larger than our cache, just start the cache at the beginning of the range
        if (count >= _cacheSize)
        {
            newStartIndex = start;
        }
        // If the requested range is before our current window
        else if (start < _cacheStartIndex)
        {
            // Try to center the window on the request
            newStartIndex = Math.Max(0, requestCenter - _cacheSize / 2);
            // But never move past the start of the request
            newStartIndex = Math.Min(newStartIndex, start);
        }
        // If the requested range is after our current window
        else if (requestEnd > _cacheStartIndex + _cacheSize)
        {
            // Try to center the window on the request
            newStartIndex = Math.Max(0, requestCenter - _cacheSize / 2);
            // But never move backward if we're scrolling forward
            newStartIndex = Math.Max(newStartIndex, start - (_cacheSize - count));
        }
        // Otherwise, we're within the current window, so don't move it
        else
        {
            return;
        }
        
        // Ensure we never go negative
        newStartIndex = Math.Max(0, newStartIndex);
        
        // If we're not actually changing the window, do nothing
        if (newStartIndex == _cacheStartIndex)
            return;

        // Calculate shift amount (positive means moving window forward, sliding cache backward)
        int shift = newStartIndex - _cacheStartIndex;
        
        if (shift != 0)
        {
            // Shift the cache
            ShiftCache(shift);
            
            // Update the cache start index
            _cacheStartIndex = newStartIndex;
            
            // Update fetched ranges to reflect the new window position
            UpdateFetchedRangesAfterShift();
        }
    }

    /// <summary>
    /// Updates the set of fetched ranges after the cache window has shifted.
    /// </summary>
    private void UpdateFetchedRangesAfterShift()
    {
        var newFetchedRanges = new HashSet<(int Start, int End)>();
        
        foreach (var range in _fetchedRanges)
        {
            // If the range is completely outside our new cache window, discard it
            if (range.End <= _cacheStartIndex || range.Start >= _cacheStartIndex + _cacheSize)
                continue;
                
            // Otherwise, keep the part that overlaps with our cache window
            int newStart = Math.Max(range.Start, _cacheStartIndex);
            int newEnd = Math.Min(range.End, _cacheStartIndex + _cacheSize);
            
            if (newStart < newEnd)
            {
                newFetchedRanges.Add((newStart, newEnd));
            }
        }
        
        _fetchedRanges.Clear();
        foreach (var range in newFetchedRanges)
        {
            _fetchedRanges.Add(range);
        }
    }

    /// <summary>
    /// Shifts the cache contents to match the new cache window.
    /// </summary>
    private void ShiftCache(int shift)
    {
        if (shift == 0) return;
        
        // For large shifts that exceed cache size, just clear everything
        if (Math.Abs(shift) >= _cacheSize)
        {
            for (int i = 0; i < _cacheSize; i++)
            {
                _cache[i] = default;
            }
            return;
        }
        
        if (shift > 0)
        {
            // Moving window forward (shifting data backward)
            // We start from the beginning and move data left
            for (int i = 0; i < _cacheSize - shift; i++)
            {
                _cache[i] = _cache[i + shift];
            }
            
            // Clear the end positions that now need new data
            for (int i = _cacheSize - shift; i < _cacheSize; i++)
            {
                _cache[i] = default;
            }
        }
        else
        {
            // Moving window backward (shifting data forward)
            shift = -shift; // Make positive for easier math
            
            // We start from the end and move data right
            for (int i = _cacheSize - 1; i >= shift; i--)
            {
                _cache[i] = _cache[i - shift];
            }
            
            // Clear the beginning positions that now need new data
            for (int i = 0; i < shift; i++)
            {
                _cache[i] = default;
            }
        }
    }

    /// <summary>
    /// Gets the list of ranges within the requested range that are not yet in the cache.
    /// </summary>
    private List<(int Start, int End)> GetMissingRanges(int start, int count)
    {
        var result = new List<(int Start, int End)>();
        if (count <= 0) return result;
        
        int end = start + count;
        
        // Clamp to cache bounds
        int clampedStart = Math.Max(start, _cacheStartIndex);
        int clampedEnd = Math.Min(end, _cacheStartIndex + _cacheSize);
        
        if (clampedStart >= clampedEnd) return result;
        
        // Start with the full clamped range
        var pendingRanges = new List<(int Start, int End)> { (clampedStart, clampedEnd) };
        
        // Remove parts that are already in the fetched ranges
        for (int i = 0; i < pendingRanges.Count; i++)
        {
            var range = pendingRanges[i];
            bool split = false;
            
            // Check against all fetched ranges
            foreach (var fetchedRange in _fetchedRanges)
            {
                // If this range is fully contained in a fetched range, remove it
                if (range.Start >= fetchedRange.Start && range.End <= fetchedRange.End)
                {
                    pendingRanges.RemoveAt(i);
                    i--;
                    split = true;
                    break;
                }
                
                // If this range partially overlaps with a fetched range, split it
                if (range.Start < fetchedRange.End && range.End > fetchedRange.Start)
                {
                    pendingRanges.RemoveAt(i);
                    
                    // Add the part before the fetched range
                    if (range.Start < fetchedRange.Start)
                    {
                        pendingRanges.Add((range.Start, fetchedRange.Start));
                    }
                    
                    // Add the part after the fetched range
                    if (range.End > fetchedRange.End)
                    {
                        pendingRanges.Add((fetchedRange.End, range.End));
                    }
                    
                    i--;
                    split = true;
                    break;
                }
            }
            
            // If we didn't split by fetched ranges, check actual cache contents
            if (!split)
            {
                // Check each position in the cache
                int rangeStart = range.Start;
                while (rangeStart < range.End)
                {
                    // Find the next null item
                    int nullStart = rangeStart;
                    int cacheIndex = nullStart - _cacheStartIndex;
                    
                    // Skip over non-null items
                    while (cacheIndex >= 0 && cacheIndex < _cacheSize && _cache[cacheIndex] != null)
                    {
                        nullStart++;
                        cacheIndex = nullStart - _cacheStartIndex;
                    }
                    
                    // If we found a gap, find where it ends
                    if (nullStart < range.End)
                    {
                        int nullEnd = nullStart;
                        cacheIndex = nullEnd - _cacheStartIndex;
                        
                        // Find the end of the null sequence
                        while (nullEnd < range.End && (cacheIndex < 0 || cacheIndex >= _cacheSize || _cache[cacheIndex] == null))
                        {
                            nullEnd++;
                            cacheIndex = nullEnd - _cacheStartIndex;
                        }
                        
                        // Add this missing range
                        if (nullEnd > nullStart)
                        {
                            result.Add((nullStart, nullEnd));
                        }
                        
                        rangeStart = nullEnd;
                    }
                    else
                    {
                        // No more gaps
                        break;
                    }
                }
                
                // Remove this range as we've processed it directly
                pendingRanges.RemoveAt(i);
                i--;
            }
        }
        
        // Add any remaining pending ranges
        result.AddRange(pendingRanges);
        
        return result;
    }

    /// <summary>
    /// Fetches a range of data from the server and updates the cache.
    /// </summary>
    private async Task<bool> FetchDataRange(int start, int count)
    {
        if (count <= 0) return true;
        
        // Don't fetch outside cache bounds
        if (start < _cacheStartIndex || start + count > _cacheStartIndex + _cacheSize)
        {
            start = Math.Max(start, _cacheStartIndex);
            int end = Math.Min(start + count, _cacheStartIndex + _cacheSize);
            count = end - start;
            
            if (count <= 0) return true;
        }
        
        // Check if we've already fetched this range
        if (_fetchedRanges.Any(r => r.Start <= start && r.End >= start + count))
        {
            return true;
        }
        
        // Fetch from server
        var result = await _node.GetJsonAsync<ModelQueryResponse<TModel>>(
            $"{_route}?skip={start}&take={count}");
            
        if (!result.Success || result.Data.Items == null)
        {
            return false;
        }
        
        // Update total count
        _totalCount = result.Data.TotalCount;
        
        // Sync to client cache
        result.Data.Sync(_node.Client);
        
        // Update cache
        for (int i = 0; i < result.Data.Items.Count; i++)
        {
            int globalIndex = start + i;
            int cacheIndex = globalIndex - _cacheStartIndex;
            
            if (cacheIndex >= 0 && cacheIndex < _cacheSize)
            {
                _cache[cacheIndex] = result.Data.Items[i];
            }
        }
        
        // Record this range as fetched
        _fetchedRanges.Add((start, start + result.Data.Items.Count));
        
        // Merge overlapping fetched ranges
        MergeFetchedRanges();
        
        return true;
    }
    
    /// <summary>
    /// Merges overlapping or adjacent fetched ranges to keep the set size small.
    /// </summary>
    private void MergeFetchedRanges()
    {
        bool merged;
        do
        {
            merged = false;
            var ranges = _fetchedRanges.ToList();
            
            for (int i = 0; i < ranges.Count; i++)
            {
                for (int j = i + 1; j < ranges.Count; j++)
                {
                    var r1 = ranges[i];
                    var r2 = ranges[j];
                    
                    // Check if ranges overlap or are adjacent
                    if (r1.Start <= r2.End && r2.Start <= r1.End)
                    {
                        // Merge ranges
                        _fetchedRanges.Remove(r1);
                        _fetchedRanges.Remove(r2);
                        
                        int newStart = Math.Min(r1.Start, r2.Start);
                        int newEnd = Math.Max(r1.End, r2.End);
                        
                        _fetchedRanges.Add((newStart, newEnd));
                        
                        merged = true;
                        break;
                    }
                }
                
                if (merged) break;
            }
        } while (merged);
    }
}
