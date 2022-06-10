using Valour.Database.Items.Planets;

namespace Valour.Database.Nodes;

public class DeployedNode
{
    /// <summary>
    /// The instance representing this node
    /// </summary>
    public static DeployedNode Instance;

    /// <summary>
    /// All of the planets this node is responsible for
    /// </summary>
    public Dictionary<ulong, Planet> Planets = new();

    /// <summary>
    /// This is the name of this node
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// This is the address of this node
    /// </summary>
    public readonly string Address;

    public DeployedNode(string name)
    {
        Name = name;
        Address = $"https://{name}.nodes.valour.gg";

        Instance = this;
    }

}

