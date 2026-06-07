using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;

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

    public async Task<TaskResult<PlanetReport>> PostPlanetReportAsync(PlanetReport report, Planet planet)
    {
        var response = await planet.Node.PostAsyncWithResponse<PlanetReport>(
            ISharedPlanetReport.GetBaseRoute(planet.Id),
            report);

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    public async Task<TaskResult<PlanetReport>> ResolvePlanetReportAsync(
        Planet planet,
        long reportId,
        ReportResolution resolution,
        string notes)
    {
        var response = await planet.Node.PostAsyncWithResponse<PlanetReport>(
            ISharedPlanetReport.GetResolveRoute(planet.Id, reportId),
            new ResolvePlanetReportRequest
            {
                Resolution = resolution,
                Notes = notes
            });

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    public async Task<Message> GetPlanetReportMessageAsync(Planet planet, long reportId)
    {
        var response = await planet.Node.GetJsonAsync<Message>(
            $"{ISharedPlanetReport.GetIdRoute(planet.Id, reportId)}/message");

        return response.Data?.Sync(_client);
    }

    public async Task<TaskResult<PlanetReport>> KickPlanetReportAsync(Planet planet, long reportId, string notes)
    {
        var response = await planet.Node.PostAsyncWithResponse<PlanetReport>(
            ISharedPlanetReport.GetKickRoute(planet.Id, reportId),
            new PlanetReportActionRequest
            {
                Notes = notes
            });

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    public async Task<TaskResult<PlanetReport>> BanPlanetReportAsync(
        Planet planet,
        long reportId,
        string reason,
        string notes,
        DateTime? timeExpires = null)
    {
        var response = await planet.Node.PostAsyncWithResponse<PlanetReport>(
            ISharedPlanetReport.GetBanRoute(planet.Id, reportId),
            new PlanetReportActionRequest
            {
                Reason = reason,
                Notes = notes,
                TimeExpires = timeExpires
            });

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }
}
