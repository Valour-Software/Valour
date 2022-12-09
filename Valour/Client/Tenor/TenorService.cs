using Valour.TenorTwo;
using Valour.TenorTwo.Models;

namespace Valour.Client.Tenor;

public class TenorService
{
    /// <summary>
    /// The key for Valour using the Tenor API
    /// </summary>
    private const string TenorKey = "AIzaSyCpYasE9IZNecc7ZPEjHTpOVssJT1aUC_4";
    
    /// <summary>
    /// Client for interacting with the Tenor API
    /// </summary>
    public TenorClient Client => _client;
    
    private readonly HttpClient _httpClient;
    private readonly TenorClient _client;

    public static readonly List<MediaFormatType> Formats = new()
    {
        MediaFormatType.gif,
        MediaFormatType.tinygif
    };

    public HttpClient GetHttpClient()
    {
        return _httpClient;
    }
    
    public TenorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _client = new TenorClient(TenorKey, "valour", http: httpClient);
    }
}