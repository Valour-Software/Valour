using Valour.Sdk.Client;
using Valour.Shared;

namespace Valour.Sdk.Services;

public class SafetyService : ServiceBase
{
    private static readonly LogOptions LogOptions = new (
        "SafetyService",
        "#036bfc",
        "#fc0356",
        "#fc8403"
    );
    
    private readonly ValourClient _client;

    public SafetyService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }
    
    public async Task<TaskResult> PostReportAsync(Report report)
    {
        var response = await _client.PrimaryNode.PostAsync("api/reports", report);
        return response;
    }
}