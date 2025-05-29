using Valour.Sdk.Client;
using Valour.Sdk.Models.Themes;
using Valour.Sdk.Nodes;
using Valour.Shared.Queries;

namespace Valour.Sdk.ModelLogic.QueryEngines;

public class ThemeMetaQueryEngine : ModelQueryEngine<ThemeMeta>
{
    public ThemeMetaQueryEngine(Node node, int cacheSize = 100) : 
        base(node, "api/themes/query", cacheSize)
    {
    }
}