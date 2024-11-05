using Valour.Sdk.Client;
using Valour.Shared.Authorization;

namespace Valour.Sdk.Services;

public class OauthService : ServiceBase
{
    private readonly LogOptions _logOptions = new(
        "OauthService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );
    
    private readonly ValourClient _client;

    public OauthService(ValourClient client)
    {
        _client = client;
        SetupLogging(_client.Logger, _logOptions);
    }
    
    public async Task<List<OauthApp>> FetchMyOauthAppAsync() =>
        (await _client.PrimaryNode.GetJsonAsync<List<OauthApp>>($"{_client.Me.IdRoute}/apps")).Data;

    public async Task<OauthApp> FetchAppAsync(long id) =>
        (await _client.PrimaryNode.GetJsonAsync<OauthApp>($"api/oauth/app/{id}")).Data;

    public async Task<PublicOauthAppData> FetchAppPublicDataAsync(long id) =>
        (await _client.PrimaryNode.GetJsonAsync<PublicOauthAppData>($"api/oauth/app/public/{id}")).Data;
}