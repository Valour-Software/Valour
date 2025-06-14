using Valour.Sdk.Nodes;

namespace Valour.Sdk.ModelLogic.QueryEngines;

public class StaffUserQueryEngine : ModelQueryEngine<User>
{
    public StaffUserQueryEngine(Node node, int cacheSize = 200) : 
        base(node, $"api/staff/users/query", cacheSize)
    {
    }
}