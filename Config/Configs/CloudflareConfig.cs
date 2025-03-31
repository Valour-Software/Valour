namespace Valour.Config.Configs;

public class CloudflareConfig
{
    public static CloudflareConfig Current;

    public CloudflareConfig()
    {
        Current = this;
    }
    
    public string? CachePurgeToken { get; set; }
    
    public string? CallsAppId { get; set; }
    public string? CallsToken { get; set; }
    
    public string? TurnTokenId { get; set; }
    public string? TurnToken { get; set; }
}