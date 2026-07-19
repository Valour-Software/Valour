namespace Valour.Config.Configs;

/// <summary>
/// Configuration for node system
/// </summary>
public class NodeConfig
{
    public static NodeConfig Instance;

    public string Key { get; set; }
    
    //#if DEBUG
    //public string Name => "debug-node";
    //#else
    public string Name { get; set; }
    //#endif
    
    public bool LogInfo { get; set; }

    public string Location { get; set; }

    /// <summary>
    /// IdGen worker id (0–1023). It needs to be distinct only among concurrent
    /// application instances that write to the same database. It is not a
    /// federation registration identifier: unrelated community nodes may reuse
    /// it, and a single-instance community node may use the default 0.
    /// </summary>
    public int WorkerId { get; set; }

    public NodeConfig()
    {
        Instance = this;
    }

    public static readonly Dictionary<string, string> NicknameMap = new()
    {
        {"valour-nodes-set-0", "emma"},
        {"valour-nodes-set-1", "jeff"},
        {"valour-nodes-set-2", "kobe"},
        {"valour-nodes-set-3", "honk"},
        {"valour-nodes-set-4", "coca"},
        {"valour-nodes-set-5", "jacob"},
        {"valour-nodes-set-6", "charlotte"},
        {"valour-nodes-set-7", "stuart"},
        {"valour-nodes-set-8", "angel"},
        {"valour-nodes-set-9", "caleb"},
        {"valour-nodes-set-10", "storm"},
        {"valour-nodes-set-11", "spike"},
        {"valour-nodes-set-12", "david"},
        {"valour-nodes-set-13", "charm"},
        {"valour-nodes-set-14", "jewel"},
    };

    public void ApplyKubeHostname(string name)
    {
        // Try to use nickname if we can find it
        NicknameMap.TryGetValue(name, out name);
        
#if !DEBUG
        this.Name = name;
        this.Location = $"https://{name}.nodes.valour.gg";
#endif
        Console.WriteLine($"Applied Kubernetes Hostname: {name}");
    }
}
