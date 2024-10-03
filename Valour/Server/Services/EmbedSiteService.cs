namespace Valour.Server.Services;

/// <summary>
/// Turns URLs into embeddable attachments
/// </summary>
public class EmbedSiteService
{
    private HttpClient _http;
    
    public EmbedSiteService(HttpClient http)
    {
        _http = http;
    }

    /* do this later
    public MessageAttachment GetAttachment(string url)
    {
        // Get hash of url
        
    }
    */
}