namespace Valour.Sdk.ModelLogic.QueryEngines;

public class PlanetBanQueryEngine : ModelQueryEngine<PlanetBan>
{
    public PlanetBanQueryEngine(Planet planet, int cacheSize = 100) : 
        base(planet.Node, $"api/planets/{planet.Id}/bans/query", cacheSize)
    {
    }
}