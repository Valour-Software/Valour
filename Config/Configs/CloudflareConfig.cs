namespace Valour.Config.Configs;

public class CloudflareConfig
{
    public static CloudflareConfig Instance { get; private set; } = null!;

    public string ZoneId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    
    public CloudflareConfig()
    {
        Instance = this;
    }
}