using System.Net;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PagedReader<TItem, TResponse>: IAsyncEnumerable<TItem>, IAsyncEnumerator<TItem>
    where TResponse : QueryResponse<TItem>, new()
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
            int currentPage = _currentPageIndex;
            _currentPageIndex++;

            if (_postData is not null)
            {
                response = await Node.PostAsyncWithResponse<TResponse>(_url + BuildQueryStringForPage(currentPage), _postData);
            }
            else
            {
                response = await Node.GetJsonAsync<TResponse>(_url + BuildQueryStringForPage(currentPage));
            }

            if (!response.Success)
            {
                Log($"Failed to get page {currentPage} of {typeof(TItem).Name}: {response.Message}", "yellow");
                return new TResponse()
                {
                    Items = new List<TItem>(),
                    TotalCount = 0
                };
            }

            ProcessPage(response.Data);

            _currentPage = response.Data;
            _totalCount = _currentPage.TotalCount;

            _currentIndex = -1;

            return _currentPage;
        }
        catch (Exception ex)
        {
            Log($"Exception occurred while fetching page {_currentPageIndex - 1}: {ex.Message}", "red");
            return new TResponse()
            {
                Items = new List<TItem>(),
                TotalCount = 0
            };
        }
    }
    
    private string BuildQueryStringForPage(int pageIndex)
    {
        var queryParameters = new List<string>
        {
            $"skip={pageIndex * _pageSize}",
            $"take={_pageSize}"
        };

        if (_parameters is not null)
        {
            queryParameters.AddRange(_parameters.Select(p => $"{p.Key}={WebUtility.UrlEncode(p.Value)}"));
        }

        return "?" + string.Join("&", queryParameters);
    }


    
    public virtual void ProcessPage(TResponse page)
    {
        // Do nothing by default
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
        int indexInPage = index % _pageSize;
        
        Console.WriteLine($"[PagedReader] AtIndexAsync: Request index={index}, pageSize={_pageSize}, targetPage={targetPageIndex}, indexInPage={indexInPage}");

        // Only reload page if needed
        if (_currentPage == null || _currentPageIndex != targetPageIndex)
        {
            var page = await GetPageAtIndexAsync(targetPageIndex);
            if (page == null || page.Items.Count == 0)
            {
                Console.WriteLine($"[PagedReader] No page data at targetPage={targetPageIndex}");
                return default;
            }

            _currentPage = page;
            _currentPageIndex = targetPageIndex;
            _totalCount = _currentPage.TotalCount;
            Console.WriteLine($"[PagedReader] Loaded page {targetPageIndex} with {page.Items.Count} items (totalCount={_totalCount})");
        }

        if (indexInPage < _currentPage.Items.Count)
        {
            Console.WriteLine($"[PagedReader] Returning item {indexInPage} of page {_currentPageIndex} (itemName={(typeof(TItem).GetProperty("Name")?.GetValue(_currentPage.Items[indexInPage]) ?? _currentPage.Items[indexInPage])})");
            return _currentPage.Items[indexInPage];
        }
        else
        {
            Console.WriteLine($"[PagedReader] indexInPage={indexInPage} out of range for page items={_currentPage.Items.Count}");
            return default;
        }
    }


   
   private async Task<TResponse> GetPageAtIndexAsync(int pageIndex) 
   { 
       string queryString = BuildQueryStringForPage(pageIndex); 
       TaskResult<TResponse> response; 
       try 
       { 
           if (_postData is not null)
            response = await Node.PostAsyncWithResponse<TResponse>(_url + queryString, _postData);
           else
               response = await Node.GetJsonAsync<TResponse>(_url + queryString);

           if (!response.Success)
           {
               Log($"Failed to get page {pageIndex} of {typeof(TItem).Name}: {response.Message}", "yellow");
               return new TResponse()
               {
                   Items = new List<TItem>(),
                   TotalCount = 0
               };
           }

           ProcessPage(response.Data);
           return response.Data;
       }
       catch (Exception ex)
       {
           Log($"Exception occurred while fetching page {pageIndex}: {ex.Message}", "red");
           return new TResponse()
           {
               Items = new List<TItem>(),
               TotalCount = 0
           };
       } 
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

    public void ResetReader()
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
        return ValueTask.CompletedTask;
    }
}

public class PagedReader<T> : PagedReader<T, QueryResponse<T>>
{
    public PagedReader(Node node, string url, int pageSize = 20, Dictionary<string, string> parameters = null, object postData = null) : 
        base(node, url, pageSize, parameters, postData)
    {
    }
}

public class PagedModelReader<TModel> : PagedReader<TModel, ModelQueryResponse<TModel>>
    where TModel : ClientModel<TModel>
{
    public PagedModelReader(Node node, string url, int pageSize = 20, Dictionary<string, string> parameters = null, object postData = null) : base(node, url, pageSize, parameters, postData)
    {
    }

    public override void ProcessPage(ModelQueryResponse<TModel> page)
    {
        page.Sync(Node.Client);
    }
}
