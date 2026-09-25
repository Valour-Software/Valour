using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Requests;
using Valour.Shared;

namespace Valour.Sdk.Services;

public class AutomodService : ServiceBase
{
    private readonly ValourClient _client;

    public AutomodService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, new("Automod", "#009900"));
    }

    public async Task<TaskResult<AutomodTrigger>> CreateTriggerAsync(AutomodTrigger trigger)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(trigger.PlanetId);
        var result = await planet.Node.PostAsyncWithResponse<AutomodTrigger>(trigger.BaseRoute, trigger);
        await ApproveForEncryptionAsync(planet, result);
        return result;
    }

    public async Task<TaskResult<AutomodTrigger>> CreateTriggerAsync(CreateAutomodTriggerRequest request)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(request.Trigger.PlanetId);
        var result = await planet.Node.PostAsyncWithResponse<AutomodTrigger>($"{request.Trigger.BaseRoute}/full", request);
        await ApproveForEncryptionAsync(planet, result);
        return result;
    }

    /// <summary>
    /// Saves changes to a trigger and, in an invite-only planet, approves its
    /// new text for encrypted channels.
    /// </summary>
    public async Task<TaskResult<AutomodTrigger>> UpdateTriggerAsync(AutomodTrigger trigger)
    {
        var result = await trigger.UpdateAsync();
        var planet = await _client.PlanetService.FetchPlanetAsync(trigger.PlanetId);
        await ApproveForEncryptionAsync(planet, result);
        return result;
    }

    // In invite-only planets, word and command triggers only work in encrypted
    // channels once an admin approves their text in the membership log.
    private async Task ApproveForEncryptionAsync(Planet planet, TaskResult<AutomodTrigger> result)
    {
        if (!result.Success || result.Data is null || planet is null)
            return;

        var approved = await _client.E2eeService.ApproveAutomodTriggersAsync(planet, [result.Data]);
        if (!approved.Success)
            LogWarning($"Trigger {result.Data.Id} was saved but not approved for encrypted channels: {approved.Message}");
    }

    public async Task<TaskResult<AutomodAction>> CreateActionAsync(AutomodAction action)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(action.PlanetId);
        return await planet.Node.PostAsyncWithResponse<AutomodAction>(action.BaseRoute, action);
    }

    public ModelQueryEngine<AutomodTrigger> GetTriggerQueryEngine(Planet planet) =>
        new ModelQueryEngine<AutomodTrigger>(planet.Node, $"api/planets/{planet.Id}/automod/triggers/query");

    public ModelQueryEngine<AutomodAction> GetActionQueryEngine(Planet planet, Guid triggerId) =>
        new ModelQueryEngine<AutomodAction>(planet.Node, $"api/planets/{planet.Id}/automod/triggers/{triggerId}/actions/query");

    public ModelQueryEngine<ModerationAuditLog> GetAuditLogQueryEngine(Planet planet) =>
        new ModelQueryEngine<ModerationAuditLog>(planet.Node, $"api/planets/{planet.Id}/moderation/audit/query");
}
