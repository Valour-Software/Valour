using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
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
        return await planet.Node.PostAsyncWithResponse<AutomodTrigger>(trigger.BaseRoute, trigger);
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
}
