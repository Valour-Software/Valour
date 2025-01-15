using Valour.Config.Configs;

namespace Valour.Config;

public static class ConfigLoader
{
    public static void Main(string[] args)
    {
        LoadConfigs();
    }
    
    /// <summary>
    /// Loads the json configs for services
    /// </summary>
    public static void LoadConfigs()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        config.GetSection("CDN").Get<CdnConfig>();
        config.GetSection("Database").Get<DbConfig>();
        config.GetSection("Email").Get<EmailConfig>();
        config.GetSection("Vapid").Get<VapidConfig>();
        config.GetSection("Node").Get<NodeConfig>();
        config.GetSection("Redis").Get<RedisConfig>();
        config.GetSection("Paypal").Get<PaypalConfig>();

        // Override with Kubernetes node details
        var nodeName = Environment.GetEnvironmentVariable("NODE_NAME");
        if (nodeName is not null)
        {
            NodeConfig.Instance.ApplyKubeHostname(nodeName);
        }
    }
}