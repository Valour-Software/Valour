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
        
        LoadTestDbConfig();
        
        var testRedis = Environment.GetEnvironmentVariable("TEST_REDIS");
        if (testRedis is not null)
        {
            RedisConfig.Current.ConnectionString = testRedis;
            Console.WriteLine($"Using test redis: {testRedis}");
        }
        
        var testEmail = Environment.GetEnvironmentVariable("TEST_EMAIL") ?? "fake-value";
        if (EmailConfig.Instance is null)
        {
            new EmailConfig();
            EmailConfig.Instance!.ApiKey = testEmail;
            Console.WriteLine("Using test email");
        }
    }

    public static void LoadTestDbConfig()
    {
        // Check for integration test database details
        var testDb = Environment.GetEnvironmentVariable("TEST_DB");
        if (testDb is not null)
        {
            DbConfig.Instance.Database = testDb;
            Console.WriteLine($"Using test database: {testDb}");
        }
        
        var testDbUser = Environment.GetEnvironmentVariable("TEST_DB_USER");
        if (testDbUser is not null)
        {
            DbConfig.Instance.Username = testDbUser;
            Console.WriteLine($"Using test database user: {testDbUser}");
        }
        
        var testDbPass = Environment.GetEnvironmentVariable("TEST_DB_PASS");
        if (testDbPass is not null)
        {
            DbConfig.Instance.Password = testDbPass;
            Console.WriteLine("Using test database password");
        }
        
        var testDbHost = Environment.GetEnvironmentVariable("TEST_DB_HOST");
        if (testDbHost is not null)
        {
            DbConfig.Instance.Host = testDbHost;
            Console.WriteLine($"Using test database host: {testDbHost}");
        }
    }
}