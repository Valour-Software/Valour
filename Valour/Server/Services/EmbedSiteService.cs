using Valour.Sdk.Models;
using Valour.Server.Cdn;

namespace Valour.Server.Services;

/// <summary>
/// Turns URLs into embeddable attachments
/// </summary>
public class EmbedSiteService
{
    private CdnDb _cdnDb;
    private HttpClient _http;
    
    public EmbedSiteService(HttpClient http, CdnDb cdnDb)
    {
        _cdnDb = cdnDb;
        _http = http;
    }

    /* do this later
    public MessageAttachment GetAttachment(string url)
    {
        // Get hash of url
        
    }
    */
}