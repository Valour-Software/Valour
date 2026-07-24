using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Villages;

namespace Valour.Sdk.Services;

public class VillageService : ServiceBase
{
    private readonly ValourClient _client;

    public VillageService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, new LogOptions("VillageService", "#6f8f4d", "#a3333e", "#a39433"));
    }

    public Task<TaskResult<VillagePocScene>> FetchProofOfConceptSceneAsync(long planetId) =>
        _client.PrimaryNode.GetJsonAsync<VillagePocScene>($"api/planets/{planetId}/village/poc");
}
