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

    /// <summary>
    /// Messages the reporter revealed, with whether the server could prove
    /// the author sent them.
    /// </summary>
    public async Task<List<Valour.Sdk.E2ee.ReportEvidenceDto>> GetPlanetReportEvidenceAsync(Planet planet, long reportId)
    {
        var response = await planet.Node.GetJsonAsync<List<Valour.Sdk.E2ee.ReportEvidenceDto>>(
            $"{ISharedPlanetReport.GetIdRoute(planet.Id, reportId)}/evidence", cacheDurationMs: null);
        return response.Success ? response.Data ?? [] : [];
    }

    public async Task<Message> GetPlanetReportMessageAsync(Planet planet, long reportId)
    {
        var response = await planet.Node.GetJsonAsync<Message>(
            $"{ISharedPlanetReport.GetIdRoute(planet.Id, reportId)}/message");
        if (response.Data is null)
            return null;

        // Moderators are members, so they can read encrypted messages here.
        await _client.E2eeService.DecryptAsync(response.Data);
        return response.Data.Sync(_client);
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
        {
            response.Data?.Sync(_client);
            await RecordRemovalAsync(planet, response.Data?.ReportedUserId);
        }

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
        {
            response.Data?.Sync(_client);
            await RecordRemovalAsync(planet, response.Data?.ReportedUserId);
        }

        return response;
    }

    /// <summary>
    /// Removes a kicked or banned member from an invite-only planet's signed
    /// membership log, so they cannot receive keys if they rejoin.
    /// </summary>
    private async Task RecordRemovalAsync(Planet planet, long? userId)
    {
        if (userId is null)
            return;

        var result = await _client.E2eeService.RemovePlanetMembersAsync(planet, [userId.Value]);
        if (!result.Success)
            LogWarning("Could not remove the member from the planet's membership log: " + result.Message);
    }
}
