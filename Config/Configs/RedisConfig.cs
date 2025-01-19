namespace Valour.Config.Configs;

public class RedisConfig
{
    public static RedisConfig Current { get; set; }

    public RedisConfig()
    {
        Current = this;
    }
    
    public string ConnectionString { get; set; }
}