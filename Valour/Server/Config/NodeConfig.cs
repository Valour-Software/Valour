namespace Valour.Server.Config;

/// <summary>
/// Configuration for node system
/// </summary>
public class NodeConfig
{
    public static NodeConfig Instance;

    public string Key { get; set; }
    
    #if DEBUG
    public string Name => "debug-node";
    #else
    public string Name { get; set; }
    #endif
    
    
    public string Location { get; set; }

    public NodeConfig()
    {
        Instance = this;
    }
}
