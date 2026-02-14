namespace Valour.Config.Configs;

public class CloudflareConfig
{
    public static CloudflareConfig Instance { get; private set; } = null!;

    public string ZoneId { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    public string RealtimeAccountId { get; set; } = string.Empty;
    public string RealtimeAppId { get; set; } = string.Empty;
    public string RealtimeApiToken { get; set; } = string.Empty;
    public string RealtimePresetName { get; set; } = "group_call_host";
    
    public CloudflareConfig()
    {
        Instance = this;
    }
}
