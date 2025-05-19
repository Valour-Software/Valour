#nullable enable

using System.Net;
using Valour.Sdk.Nodes;
using Valour.Shared.Queries;

namespace Valour.Sdk.ModelLogic;

public interface IModelQueryEngine<TModel>
    where TModel : ClientModel<TModel>
{
    public Task<ModelQueryResponse<TModel>> GetItemsAsync(int skip, int take);
    public Task<IReadOnlyList<TModel>> GetPageAsync(int pageIndex, int pageSize);
    public Task<IReadOnlyList<TModel>> NextPageAsync();
    public Task<IReadOnlyList<TModel>> PreviousPageAsync();
    public Task<IReadOnlyList<TModel>> RefreshCurrentPageAsync();
    public Task<TModel> GetAtIndexAsync(int index);
    public Task<int> GetTotalCountAsync();
}

public class ModelQueryEngine<TModel> : IModelQueryEngine<TModel>, IAsyncEnumerable<TModel>
    where TModel : ClientModel<TModel>
{
    private readonly string _route;
    private readonly Node _node;
    private readonly int _cacheSize;
    private int? _totalCount;

    // Sliding cache
    private readonly TModel[] _cache;
    private int _cacheStartIndex;
    private readonly HashSet<(int Start, int End)> _fetchedRanges = new();

    // Query options
    private QueryOptions? _options;

    // Paging state
    private int _currentPageIndex;
    private int _pageSize = 20;
    private List<TModel>? _currentPage;

    public ModelQueryEngine(Node node, string route, int cacheSize = 200)
    {
        _route = route;
        _node = node;
        _cacheSize = cacheSize;
        _cache = new TModel[_cacheSize];
        _options = new();
    }

    // --- Filter & Sort API ---
    
    /// <summary>
    /// Resets the engine with new options
    /// </summary>
    public void ApplyOptions()
    {
        ResetPaging(); 
    }

    public void SetFilter(string key, string? value, bool apply = true)
    {
        _options ??= new();
        _options.Filters ??= new();
        
        if (string.IsNullOrEmpty(value))
            _options.Filters.Remove(key);
        else
            _options.Filters[key] = value;
        
        if (apply)
            ApplyOptions();
    }

    public void SetSort(string field, bool descending = false, bool apply = true)
    {
        _options ??= new();
        _options.Sort = new QuerySort
        {
            Field = field,
            Descending = descending
        };
        
        if (apply)
            ApplyOptions();
    }
    public void ClearSort()
    {
        if (_options is not null)
        {
            _options.Sort = null;
            ApplyOptions();
        }
    }

    // --- Paging State API ---

    public int CurrentPageIndex => _currentPageIndex;
    public int PageSize => _pageSize;
    public int TotalCount => _totalCount ?? -1;
    public IReadOnlyList<TModel> CurrentPage => _currentPage ?? new List<TModel>();

    public void SetPageSize(int pageSize)
    {
        _pageSize = Math.Max(1, pageSize);
        ResetPaging();
    }

    public void ResetPaging()
    {
        _currentPageIndex = 0;
        _currentPage = null;
        _totalCount = null;
        
        // Reset cache
        _cacheStartIndex = 0;
        Array.Clear(_cache, 0, _cacheSize);
        _fetchedRanges.Clear();
    }

    // --- Paged API ---

    public async Task<IReadOnlyList<TModel>> GetPageAsync(int pageIndex, int pageSize)
    {
        _currentPageIndex = pageIndex;
        _pageSize = pageSize;
        var resp = await GetItemsAsync(pageIndex * pageSize, pageSize);
        _currentPage = resp.Items;
        _totalCount = resp.TotalCount;
        return _currentPage;
    }

    public async Task<IReadOnlyList<TModel>> NextPageAsync()
    {
        _currentPageIndex++;
        return await GetPageAsync(_currentPageIndex, _pageSize);
    }

    public async Task<IReadOnlyList<TModel>> PreviousPageAsync()
    {
        if (_currentPageIndex > 0)
            _currentPageIndex--;
        return await GetPageAsync(_currentPageIndex, _pageSize);
    }

    public async Task<IReadOnlyList<TModel>> RefreshCurrentPageAsync()
    {
        return await GetPageAsync(_currentPageIndex, _pageSize);
    }

    public bool IsFirstPage => _currentPageIndex == 0;
    public bool IsLastPage => _totalCount.HasValue && ((_currentPageIndex + 1) * _pageSize) >= _totalCount.Value;

    // --- Random Access API ---

    public async Task<TModel> GetAtIndexAsync(int index)
    {
        var resp = await GetItemsAsync(index, 1);
        return resp.Items.FirstOrDefault();
    }

    public async Task<int> GetTotalCountAsync()
    {
        if (_totalCount.HasValue)
            return _totalCount.Value;
        await GetItemsAsync(0, 1);
        return _totalCount ?? 0;
    }

    // --- Core Range API ---

    public async Task<ModelQueryResponse<TModel>> GetItemsAsync(int skip, int take)
    {
        take = Math.Max(0, Math.Min(take, 1000));
        if (take == 0)
        {
            return new ModelQueryResponse<TModel>
            {
                Items = new List<TModel>(),
                TotalCount = _totalCount ?? 0
            };
        }

        if (!IsRangeInCache(skip, take))
            AdjustCacheWindow(skip, take);

        var missing = GetMissingRanges(skip, take);
        foreach (var range in missing)
            await FetchDataRange(range.Start, range.End - range.Start);

        var items = new List<TModel>(take);
        for (int i = 0; i < take; i++)
        {
            int globalIndex = skip + i;
            int cacheIndex = globalIndex - _cacheStartIndex;
            if (cacheIndex >= 0 && cacheIndex < _cacheSize && _cache[cacheIndex] != null)
                items.Add(_cache[cacheIndex]);
        }

        return new ModelQueryResponse<TModel>
        {
            Items = items,
            TotalCount = _totalCount ?? items.Count
        };
    }

    // --- Async Enumeration API ---

    public IAsyncEnumerator<TModel> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new SlidingCacheEnumerator(this, cancellationToken);

    private class SlidingCacheEnumerator : IAsyncEnumerator<TModel>
    {
        private readonly ModelQueryEngine<TModel> _engine;
        private readonly CancellationToken _ct;
        private int _index = 0;
        private int _total = -1;
        private TModel _current;

        public SlidingCacheEnumerator(ModelQueryEngine<TModel> engine, CancellationToken ct)
        {
            _engine = engine;
            _ct = ct;
        }

        public TModel Current => _current;

        public async ValueTask<bool> MoveNextAsync()
        {
            _ct.ThrowIfCancellationRequested();

            if (_total == -1)
                _total = await _engine.GetTotalCountAsync();

            if (_index >= _total)
                return false;

            _current = await _engine.GetAtIndexAsync(_index);
            _index++;
            return _current != null;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // --- Sliding Cache Internals (with named tuples) ---

    private bool IsRangeInCache(int start, int count)
    {
        int end = start + count;
        return start >= _cacheStartIndex && end <= _cacheStartIndex + _cacheSize;
    }

    private void AdjustCacheWindow(int start, int count)
    {
        int requestCenter = start + count / 2;
        int newStart = Math.Max(0, requestCenter - _cacheSize / 2);
        if (newStart == _cacheStartIndex) return;
        ShiftCache(newStart - _cacheStartIndex);
        _cacheStartIndex = newStart;
        UpdateFetchedRangesAfterShift();
    }

    private void ShiftCache(int shift)
    {
        if (shift == 0) return;
        if (Math.Abs(shift) >= _cacheSize)
        {
            Array.Clear(_cache, 0, _cacheSize);
            return;
        }
        if (shift > 0)
        {
            Array.Copy(_cache, shift, _cache, 0, _cacheSize - shift);
            Array.Clear(_cache, _cacheSize - shift, shift);
        }
        else
        {
            shift = -shift;
            Array.Copy(_cache, 0, _cache, shift, _cacheSize - shift);
            Array.Clear(_cache, 0, shift);
        }
    }

    private void UpdateFetchedRangesAfterShift()
    {
        var newRanges = new HashSet<(int Start, int End)>();
        foreach (var range in _fetchedRanges)
        {
            int ns = Math.Max(range.Start, _cacheStartIndex);
            int ne = Math.Min(range.End, _cacheStartIndex + _cacheSize);
            if (ns < ne) newRanges.Add((Start: ns, End: ne));
        }
        _fetchedRanges.Clear();
        foreach (var r in newRanges) _fetchedRanges.Add(r);
    }

    private List<(int Start, int End)> GetMissingRanges(int start, int count)
    {
        var result = new List<(int Start, int End)>();
        int end = start + count;
        int clampedStart = Math.Max(start, _cacheStartIndex);
        int clampedEnd = Math.Min(end, _cacheStartIndex + _cacheSize);
        if (clampedStart >= clampedEnd) return result;

        var pending = new List<(int Start, int End)> { (Start: clampedStart, End: clampedEnd) };
        for (int i = 0; i < pending.Count; i++)
        {
            var range = pending[i];
            bool split = false;
            foreach (var fetched in _fetchedRanges)
            {
                if (range.Start >= fetched.Start && range.End <= fetched.End)
                {
                    pending.RemoveAt(i); i--; split = true; break;
                }
                if (range.Start < fetched.End && range.End > fetched.Start)
                {
                    pending.RemoveAt(i);
                    if (range.Start < fetched.Start)
                        pending.Add((Start: range.Start, End: fetched.Start));
                    if (range.End > fetched.End)
                        pending.Add((Start: fetched.End, End: range.End));
                    i--; split = true; break;
                }
            }
            if (!split)
            {
                int rs = range.Start;
                while (rs < range.End)
                {
                    int nullStart = rs;
                    int cacheIndex = nullStart - _cacheStartIndex;
                    while (cacheIndex >= 0 && cacheIndex < _cacheSize && _cache[cacheIndex] != null)
                    {
                        nullStart++; cacheIndex = nullStart - _cacheStartIndex;
                    }
                    if (nullStart < range.End)
                    {
                        int nullEnd = nullStart;
                        cacheIndex = nullEnd - _cacheStartIndex;
                        while (nullEnd < range.End && (cacheIndex < 0 || cacheIndex >= _cacheSize || _cache[cacheIndex] == null))
                        {
                            nullEnd++; cacheIndex = nullEnd - _cacheStartIndex;
                        }
                        if (nullEnd > nullStart)
                            result.Add((Start: nullStart, End: nullEnd));
                        rs = nullEnd;
                    }
                    else break;
                }
                pending.RemoveAt(i); i--;
            }
        }
        result.AddRange(pending);
        return result;
    }

    private async Task<bool> FetchDataRange(int start, int count)
    {
        if (count <= 0) return true;
        if (start < _cacheStartIndex || start + count > _cacheStartIndex + _cacheSize)
        {
            start = Math.Max(start, _cacheStartIndex);
            int end = Math.Min(start + count, _cacheStartIndex + _cacheSize);
            count = end - start;
            if (count <= 0) return true;
        }
        if (_fetchedRanges.Any(r => r.Start <= start && r.End >= start + count))
            return true;

        var request = new QueryRequest()
        {
            Skip = start,
            Take = count,
            Options = _options
        };
        
        var result = await _node.PostAsyncWithResponse<ModelQueryResponse<TModel>>(_route, request);
        if (!result.Success || result.Data.Items == null)
            return false;

        _totalCount = result.Data.TotalCount;
        result.Data.Sync(_node.Client);

        for (int i = 0; i < result.Data.Items.Count; i++)
        {
            int globalIndex = start + i;
            int cacheIndex = globalIndex - _cacheStartIndex;
            if (cacheIndex >= 0 && cacheIndex < _cacheSize)
                _cache[cacheIndex] = result.Data.Items[i];
        }
        _fetchedRanges.Add((Start: start, End: start + result.Data.Items.Count));
        MergeFetchedRanges();
        return true;
    }

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
                    if (r1.Start <= r2.End && r2.Start <= r1.End)
                    {
                        _fetchedRanges.Remove(r1);
                        _fetchedRanges.Remove(r2);
                        _fetchedRanges.Add((Start: Math.Min(r1.Start, r2.Start), End: Math.Max(r1.End, r2.End)));
                        merged = true;
                        break;
                    }
                }
                if (merged) break;
            }
        } while (merged);
    }
}
