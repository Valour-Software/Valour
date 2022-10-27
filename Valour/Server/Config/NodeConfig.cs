namespace Valour.Server.Config;

/// <summary>
/// Configuration for node system
/// </summary>
public class NodeConfig
{
    public static NodeConfig Instance;

    public string Key { get; set; }
    public string Name { get; set; }
    public string Location { get; set; }

    public NodeConfig()
    {
        Instance = this;
    }
}
