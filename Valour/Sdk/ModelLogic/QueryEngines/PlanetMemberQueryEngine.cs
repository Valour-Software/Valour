using Valour.Sdk.Nodes;

namespace Valour.Sdk.ModelLogic.QueryEngines;

public class PlanetMemberQueryEngine : ModelQueryEngine<PlanetMember>
{
    public PlanetMemberQueryEngine(Planet planet, int cacheSize = 200) : 
        base(planet.Node, $"api/planets/{planet.Id}/members/query", cacheSize)
    {
    }
}