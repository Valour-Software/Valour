using Valour.Sdk.Nodes;

namespace Valour.Sdk.ModelLogic.QueryEngines;

public class StaffReportQueryEngine : ModelQueryEngine<Report>
{
    public StaffReportQueryEngine(Node node, int cacheSize = 200) :
        base(node, $"api/staff/reports/query", cacheSize)
    {
    }
}