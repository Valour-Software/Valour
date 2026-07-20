using Microsoft.Extensions.Configuration;
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
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        config.GetSection("CDN").Get<CdnConfig>();
        config.GetSection("Database").Get<DbConfig>();
        config.GetSection("Email").Get<EmailConfig>();
        config.GetSection("Notifications").Get<NotificationsConfig>();
        config.GetSection("Node").Get<NodeConfig>();
        config.GetSection("Redis").Get<RedisConfig>();
        config.GetSection("Stripe").Get<StripeConfig>();
        config.GetSection("Cloudflare").Get<CloudflareConfig>();
        config.GetSection("Voice").Get<VoiceConfig>();
        config.GetSection("MediaSafety").Get<MediaSafetyConfig>();
        config.GetSection("Hosting").Get<HostingConfig>();
        config.GetSection("Bootstrap").Get<BootstrapConfig>();
        config.GetSection("Federation").Get<FederationConfig>();

        // Override with Kubernetes node details
        var nodeName = Environment.GetEnvironmentVariable("NODE_NAME");
        if (nodeName is not null)
        {
            var nodeConfig = NodeConfig.Instance ?? new NodeConfig();
            nodeConfig.Name = nodeName;
        }
        
        LoadTestDbConfig();
        
        var testRedis = Environment.GetEnvironmentVariable("TEST_REDIS");
        if (RedisConfig.Current is null)
        {
            new RedisConfig();
            RedisConfig.Current!.ConnectionString = testRedis;
            Console.WriteLine($"Using test redis: {testRedis}");
        }
        
        var testEmail = Environment.GetEnvironmentVariable("TEST_EMAIL") ?? "fake-value";
        if (EmailConfig.Instance is null)
        {
            new EmailConfig();
            EmailConfig.Instance!.ApiKey = testEmail;
            Console.WriteLine("Using test email");
        }

        if (NotificationsConfig.Current is null)
        {
            new NotificationsConfig();
        }

        if (MediaSafetyConfig.Current is null)
        {
            new MediaSafetyConfig();
        }

        if (HostingConfig.Current is null)
        {
            new HostingConfig();
        }

        // Ensure every config singleton exists even when its section is absent
        // (self-hosted instances configure only what they use). Consumers can
        // rely on empty values instead of null instances.
        if (CdnConfig.Current is null)
        {
            new CdnConfig();
        }

        if (CloudflareConfig.Instance is null)
        {
            new CloudflareConfig();
        }

        if (VoiceConfig.Current is null)
        {
            new VoiceConfig();
        }

        if (StripeConfig.Current is null)
        {
            new StripeConfig();
        }

        if (BootstrapConfig.Current is null)
        {
            new BootstrapConfig();
        }

        if (FederationConfig.Current is null)
        {
            new FederationConfig();
        }

        if (NodeConfig.Instance is null)
        {
            new NodeConfig();
        }

        // Fail loud on an out-of-range worker id rather than silently clamping
        // it. This is a local IdGen field (0–1023), not a federation-wide
        // allocation; only instances sharing one database need distinct values.
        var workerId = (NodeConfig.Instance ?? throw new InvalidOperationException(
            "Node configuration was not initialized.")).WorkerId;
        if (workerId is < 0 or > 1023)
            throw new InvalidOperationException(
                $"Node:WorkerId must be between 0 and 1023 (got {workerId}).");
    }

    public static void LoadTestDbConfig()
    {
        var dbConfig = DbConfig.Instance ?? new DbConfig();
        
        // Check for integration test database details
        var testDb = Environment.GetEnvironmentVariable("TEST_DB");
        if (testDb is not null)
        {
            dbConfig.Database = testDb;
            Console.WriteLine($"Using test database: {testDb}");
        }
        
        var testDbUser = Environment.GetEnvironmentVariable("TEST_DB_USER");
        if (testDbUser is not null)
        {
            dbConfig.Username = testDbUser;
            Console.WriteLine($"Using test database user: {testDbUser}");
        }
        
        var testDbPass = Environment.GetEnvironmentVariable("TEST_DB_PASS");
        if (testDbPass is not null)
        {
            dbConfig.Password = testDbPass;
            Console.WriteLine("Using test database password");
        }
        
        var testDbHost = Environment.GetEnvironmentVariable("TEST_DB_HOST");
        if (testDbHost is not null)
        {
            dbConfig.Host = testDbHost;
            Console.WriteLine($"Using test database host: {testDbHost}");
        }
    }
}
