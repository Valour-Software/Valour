#nullable enable

using Valour.Sdk.Client;
using Valour.Sdk.Models.Themes;
using Valour.Sdk.Nodes;
using Valour.Shared.Queries;
using Valour.Shared.Models;

namespace Valour.Sdk.ModelLogic.QueryEngines;

public class ThemeMetaDto
{
    public long Id { get; set; }
    public long AuthorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool HasCustomBanner { get; set; }
    public bool HasAnimatedBanner { get; set; }
    public string MainColor1 { get; set; } = string.Empty;
    public string PastelCyan { get; set; } = string.Empty;
}

public class ThemeMetaQueryEngine
{
    private readonly Node _node;
    private readonly int _cacheSize;
    private QueryOptions? _options;
    private int _currentPageIndex;
    private int _pageSize = 20;
    private List<Valour.Sdk.Models.Themes.ThemeMeta>? _currentPage;
    private int? _totalCount;

    public ThemeMetaQueryEngine(Node node, int cacheSize = 100)
    {
        _node = node;
        _cacheSize = cacheSize;
        _options = new();
    }

    public int CurrentPageIndex => _currentPageIndex;
    public int PageSize => _pageSize;
    public int TotalCount => _totalCount ?? -1;
    public IReadOnlyList<Valour.Sdk.Models.Themes.ThemeMeta> CurrentPage => _currentPage ?? new List<Valour.Sdk.Models.Themes.ThemeMeta>();

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
    }

    public void ApplyOptions()
    {
        ResetPaging(); 
    }

    public async Task<IReadOnlyList<Valour.Sdk.Models.Themes.ThemeMeta>> GetPageAsync(int pageIndex, int pageSize)
    {
        _currentPageIndex = pageIndex;
        _pageSize = pageSize;
        var resp = await GetItemsAsync(pageIndex * pageSize, pageSize);
        _currentPage = resp.Items;
        _totalCount = resp.TotalCount;
        return _currentPage;
    }

    public async Task<IReadOnlyList<Valour.Sdk.Models.Themes.ThemeMeta>> NextPageAsync()
    {
        if (_totalCount == -1 || (_currentPageIndex + 1) * _pageSize >= _totalCount)
            return new List<Valour.Sdk.Models.Themes.ThemeMeta>();

        return await GetPageAsync(_currentPageIndex + 1, _pageSize);
    }

    public async Task<IReadOnlyList<Valour.Sdk.Models.Themes.ThemeMeta>> PreviousPageAsync()
    {
        if (_currentPageIndex <= 0)
            return new List<Valour.Sdk.Models.Themes.ThemeMeta>();

        return await GetPageAsync(_currentPageIndex - 1, _pageSize);
    }

    public async Task<IReadOnlyList<Valour.Sdk.Models.Themes.ThemeMeta>> RefreshCurrentPageAsync()
    {
        return await GetPageAsync(_currentPageIndex, _pageSize);
    }

    public async Task<int> GetTotalCountAsync()
    {
        if (_totalCount == null)
        {
            await GetPageAsync(0, 1); // Just get one item to get the total count
        }
        return _totalCount ?? 0;
    }

    private async Task<QueryResponse<Valour.Sdk.Models.Themes.ThemeMeta>> GetItemsAsync(int skip, int take)
    {
        var request = new QueryRequest()
        {
            Skip = skip,
            Take = take,
            Options = _options
        };
        
        var result = await _node.PostAsyncWithResponse<QueryResponse<ThemeMetaDto>>("api/themes/query", request);
        if (!result.Success || result.Data.Items == null)
            return new QueryResponse<Valour.Sdk.Models.Themes.ThemeMeta>() { Items = new List<Valour.Sdk.Models.Themes.ThemeMeta>(), TotalCount = 0 };

        // Convert server-side ThemeMeta to client-side ThemeMeta
        var clientItems = result.Data.Items.Select(serverMeta => new Valour.Sdk.Models.Themes.ThemeMeta()
        {
            Id = serverMeta.Id,
            AuthorId = serverMeta.AuthorId,
            Name = serverMeta.Name,
            Description = serverMeta.Description,
            HasCustomBanner = serverMeta.HasCustomBanner,
            HasAnimatedBanner = serverMeta.HasAnimatedBanner,
            MainColor1 = serverMeta.MainColor1,
            PastelCyan = serverMeta.PastelCyan
        }).ToList();

        return new QueryResponse<Valour.Sdk.Models.Themes.ThemeMeta>()
        {
            Items = clientItems,
            TotalCount = result.Data.TotalCount
        };
    }
}