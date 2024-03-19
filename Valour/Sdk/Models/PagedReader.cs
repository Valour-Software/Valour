using System.Collections.Generic;
using System.Net;
using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PagedReader<T>
{
    private readonly string _url;
    private readonly int _pageSize;
    
    private  Dictionary<string, string> _query;
    
    private int _currentPageIndex;
    private PagedResponse<T> _currentPage;
    private int _currentIndex;
    private int _totalCount;
    
    public PagedReader(string url, int pageSize = 20, Dictionary<string, string> query = null)
    {
        _url = url;
        _pageSize = pageSize;
        _query = query;
        _currentIndex = -1; // Initializing with -1, as no item has been fetched yet.
        _totalCount = -1; // Initializing with -1, as the total count is not known yet.
    }

    public int GetCurrentIndex()
    {
        return _currentIndex;
    }
    
    public async Task<PagedResponse<T>> NextPageAsync()
    {
        var baseQuery = $"?amount={_pageSize}&page={_currentPageIndex}";
        if (_query is not null)
        {
            foreach (var (key, value) in _query)
                baseQuery += $"&{key}={WebUtility.UrlEncode(value)}";
        }

        var response = await ValourClient.PrimaryNode.GetJsonAsync<PagedResponse<T>>(_url + baseQuery);
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
    
    public void SetQuery(string key, string value)
    {
        if (_query is null)
            _query = new Dictionary<string, string>();
        
        _query[key] = value;
        
        // Reset the reader
        _currentPageIndex = 0;
        _currentIndex = -1;
        _currentPage = null;
        _totalCount = -1;
    }
    
    public async Task<int> GetTotalCount()
    {
        if (_totalCount == -1)
            await NextPageAsync(); // Load in first page
        
        return _totalCount;
    }
}