using System.Net;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

// TODO: IQueryableModel system

public class PagedReader<TItem, TResponse>: IAsyncEnumerable<TItem>, IAsyncEnumerator<TItem>
    where TResponse : PagedResponse<TItem>, new()
{
    private readonly string _url;
    private readonly int _pageSize;
    protected readonly Node Node;

    private Dictionary<string, string> _parameters;
    private object _postData;

    private int _currentPageIndex;
    private TResponse _currentPage;
    private int _currentIndex;
    private int _totalCount;

    private CancellationToken _cancellationToken;

    public PagedReader(Node node, string url, int pageSize = 20, Dictionary<string, string> parameters = null, object postData = null)
    {
        Node = node;
        _url = url;
        _pageSize = pageSize;
        _parameters = parameters ?? new Dictionary<string, string>();
        _postData = postData;
        ResetReader();
    }

    public bool IsFirstPage => _currentPageIndex == 0;

    public bool IsLastPage => _totalCount != -1 && (_currentPageIndex * _pageSize) + (_currentPage?.Items.Count ?? 0) >= _totalCount;

    public int CurrentIndex => (_currentPageIndex * _pageSize) + _currentIndex;

    public int CurrentPageIndex => _currentPageIndex;

    public int TotalCount => _totalCount;

    public void SetModel(object postData)
    {
        _postData = postData;
        ResetReader();
    }

    public void SetParameter(string key, string value)
    {
        _parameters[key] = value;
        ResetReader();
    }

    public void SetParameters(Dictionary<string, string> parameters)
    {
        _parameters = new Dictionary<string, string>(parameters);
        ResetReader();
    }

    public async Task<TResponse> NextPageAsync()
    {
        string queryString = BuildQueryString();
        TaskResult<TResponse> response;

        try
        {
            if (_postData is not null)
            {
                response = await Node.PostAsyncWithResponse<TResponse>(_url + queryString, _postData);
            }
            else
            {
                response = await Node.GetJsonAsync<TResponse>(_url + queryString);
            }

            if (!response.Success)
            {
                Log($"Failed to get page {_currentPageIndex} of {typeof(TItem).Name}: {response.Message}", "yellow");
                
                return new TResponse()
                {
                    Items = new List<TItem>(),
                    TotalCount = 0
                };
            }
            
            ProcessPage(response.Data);

            _currentPage = response.Data;
            _totalCount = _currentPage.TotalCount;

            return _currentPage;
        }
        catch (Exception ex)
        {
            Log($"Exception occurred while fetching page {_currentPageIndex}: {ex.Message}", "red");
            // TODO: Retry?
            return new TResponse()
            {
                Items = new List<TItem>(),
                TotalCount = 0
            };
        }
        finally
        {
            _currentPageIndex++;
            _currentIndex = -1; // Reset current index for the new page
        }
    }
    
    public virtual void ProcessPage(TResponse page)
    {
        // Do nothing
    }

    public async Task<TResponse> RefreshCurrentPageAsync()
    {
        if (_currentPageIndex > 0)
        {
            _currentPageIndex--;
            return await NextPageAsync();
        }
        else
        {
            return await NextPageAsync();
        }
    }

    public async Task<TResponse> PreviousPageAsync()
    {
        if (_currentPageIndex > 1)
        {
            _currentPageIndex -= 2;
            return await NextPageAsync();
        }
        else
        {
            _currentPageIndex = 0;
            return new TResponse() { Items = new List<TItem>(), TotalCount = _totalCount };
        }
    }

    public async Task<TItem> NextAsync()
    {
        _currentIndex++;

        if (_currentPage is null || _currentIndex >= _currentPage.Items.Count)
        {
            var nextPage = await NextPageAsync();

            if (nextPage is null || nextPage.Items.Count == 0)
            {
                return default;
            }

            _currentIndex = 0;
        }
        
        if (_currentPage is null || _currentPage.Items.Count == 0)
            return default;

        return _currentPage.Items[_currentIndex];
    }

    public async Task<TItem> AtIndexAsync(int index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Index cannot be negative.");

        int targetPageIndex = index / _pageSize;

        if (targetPageIndex != _currentPageIndex)
        {
            _currentPageIndex = targetPageIndex;
            _currentIndex = index % _pageSize - 1; // Will be incremented in NextAsync
            _currentPage = null;
        }
        else
        {
            _currentIndex = index % _pageSize - 1; // Will be incremented in NextAsync
        }

        return await NextAsync();
    }

    private string BuildQueryString()
    {
        var queryParameters = new List<string>
        {            
            $"skip={_currentPageIndex * _pageSize}",
            $"take={_pageSize}"
        };

        if (_parameters is not null)
        {
            queryParameters.AddRange(_parameters.Select(p => $"{p.Key}={WebUtility.UrlEncode(p.Value)}"));
        }

        return "?" + string.Join("&", queryParameters);
    }

    private void ResetReader()
    {
        _currentPageIndex = 0;
        _currentIndex = -1;
        _currentPage = null;
        _totalCount = -1;
    }

    private void Log(string message, string color)
    {
        Node.Client.Log($"[{typeof(TItem).Name} PagedReader]", message, color);
    }

    // Implementation of IAsyncEnumerable<T>
    public IAsyncEnumerator<TItem> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        ResetReader();
        return this;
    }

    // Implementation of IAsyncEnumerator<T>
    public TItem Current => _currentPage is not null && _currentIndex < _currentPage.Items.Count
        ? _currentPage.Items[_currentIndex]
        : default;

    public async ValueTask<bool> MoveNextAsync()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        _currentIndex++;

        if (_currentPage is null || _currentIndex >= _currentPage.Items.Count)
        {
            if (IsLastPage && _currentPage is not null)
            {
                return false;
            }

            var nextPage = await NextPageAsync();

            if (nextPage is null || nextPage.Items.Count == 0)
            {
                return false;
            }

            _currentIndex = 0;
        }

        return true;
    }

    public ValueTask DisposeAsync()
    {
        // Dispose of any resources if necessary
        return new ValueTask();
    }
}

public class PagedReader<T> : PagedReader<T, PagedResponse<T>>
{
    public PagedReader(Node node, string url, int pageSize = 20, Dictionary<string, string> parameters = null, object postData = null) : 
        base(node, url, pageSize, parameters, postData)
    {
    }
}

public class PagedModelReader<TModel> : PagedReader<TModel, PagedModelResponse<TModel>>
    where TModel : ClientModel<TModel>
{
    public PagedModelReader(Node node, string url, int pageSize = 20, Dictionary<string, string> parameters = null, object postData = null) : base(node, url, pageSize, parameters, postData)
    {
    }

    public override void ProcessPage(PagedModelResponse<TModel> page)
    {
        page.Sync();
    }
}