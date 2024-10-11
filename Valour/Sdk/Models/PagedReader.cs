using System.Net;
using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PagedReader<T>
{
    private readonly string _url;
    private readonly int _pageSize;
    
    private  Dictionary<string, string> _parameters;
    private object _postData;
    
    private int _currentPageIndex;
    private PagedResponse<T> _currentPage;
    private int _currentIndex;
    private int _totalCount;
    
    public PagedReader(string url, int pageSize = 20, Dictionary<string, string> parameters = null, object postData = null)
    {
        _url = url;
        _pageSize = pageSize;
        _parameters = parameters;
        _currentIndex = -1; // Initializing with -1, as no item has been fetched yet.
        _totalCount = -1; // Initializing with -1, as the total count is not known yet.
        _postData = postData;
    }
    
    public bool IsFirstPage()
    {
        return _totalCount == -1 || _currentPageIndex == 1;
    }
    
    public bool IsLastPage()
    {
        return _totalCount == -1 || _currentPageIndex * _pageSize >= _totalCount;
    }

    public int GetCurrentIndex()
    {
        return _currentIndex;
    }
    
    public int GetCurrentPageIndex()
    {
        return _currentPageIndex;
    }
    
    public async Task<PagedResponse<T>> NextPageAsync()
    {
        var baseQuery = $"?amount={_pageSize}&page={_currentPageIndex}";
        if (_parameters is not null)
        {
            foreach (var (key, value) in _parameters)
                baseQuery += $"&{key}={WebUtility.UrlEncode(value)}";
        }
        
        TaskResult<PagedResponse<T>> response;

        if (_postData is not null)
        {
            response = await ValourClient.PrimaryNode.PostAsyncWithResponse<PagedResponse<T>>(_url + baseQuery, _postData);
        }
        else
        {
            response = await ValourClient.PrimaryNode.GetJsonAsync<PagedResponse<T>>(_url + baseQuery);
        }

        _currentPageIndex++;

        if (!response.Success)
        {
            await Logger.Log($"Failed to get page {_currentPageIndex} of {typeof(T)}: {response.Message}", "yellow");
            return PagedResponse<T>.Empty;
        }

        _currentPage = response.Data;
        _totalCount = _currentPage.TotalCount;
        
        return _currentPage;
    }
    
    public async Task<PagedResponse<T>> RefreshCurrentPageAsync()
    {
        if (_currentPageIndex == 0)
            return await NextPageAsync();
        
        _currentPageIndex--;
        return await NextPageAsync();
    }

    public async Task<PagedResponse<T>> PreviousPageAsync()
    {
        if (_currentPageIndex < 2)
        {
            return new PagedResponse<T>()
            {
                Items = new List<T>(),
                TotalCount = _totalCount
            };
        }

        _currentPageIndex -= 2;
        return await NextPageAsync();
    }
    
    public async Task<T> NextAsync()
    {
        _currentIndex++;
        if (_currentPage == null || _currentIndex >= _currentPage.Items.Count)
        {
            var nextPage = await NextPageAsync();
            if (nextPage == null || nextPage.Items.Count == 0)
                return default;
            
            _currentPage = nextPage;
            _currentIndex = 0;
        }

        return _currentPage.Items[_currentIndex];
    }
    
    public async Task<T> AtIndexAsync(int index)
    {
        if (index < 0)
            throw new IndexOutOfRangeException("Index cannot be negative.");
        
        // Load the required page directly
        while (_currentPage == null || index >= (_currentPageIndex * _pageSize) + _currentPage.Items.Count)
        {
            var nextPage = await NextPageAsync();
            if (nextPage == null || nextPage.Items.Count == 0)
                return default;

            _currentPage = nextPage;
        }

        // Update the current index
        _currentIndex = index;

        // Return the item at the specified index
        return _currentPage.Items[_currentIndex % _pageSize];
    }
    
    public void SetModel(object postData)
    {
        _postData = postData;
        
        ResetReader();
    }
    
    public void SetParameter(string key, string value)
    {
        if (_parameters is null)
            _parameters = new Dictionary<string, string>();
        
        _parameters[key] = value;
        
        ResetReader();
    }
    
    public void SetParameters(Dictionary<string, string> query)
    {
        _parameters = query;
        
        ResetReader();
    }

    private void ResetReader()
    {
        _currentPageIndex = 0;
        _currentIndex = -1;
        _currentPage = null;
        _totalCount = -1;
    }
    
    public int GetTotalCount()
    {
        return _totalCount;
    }
}