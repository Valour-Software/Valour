namespace Valour.Server.Config;

public class RedisConfig
{
    public static RedisConfig Current { get; set; }

    public RedisConfig()
    {
        Current = this;
    }
    
    public string ConnectionString { get; set; }
}