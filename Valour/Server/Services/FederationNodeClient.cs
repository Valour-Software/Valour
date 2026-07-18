using System.Net.Http.Json;
using Valour.Config.Configs;
using Valour.Server.Api.Dynamic;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Node-side client for server-to-server calls to the hub. Authenticates with
/// an internally-minted, self-signed S2S token — no user or staff involvement,
/// so a verified community node can register and update its planets on day one.
/// Every call is best-effort: failures are surfaced to the caller, which logs
/// and lets reconciliation retry rather than blocking local operations.
/// </summary>
public class FederationNodeClient
{
    private readonly FederationNodeService _nodeService;
    private readonly FederationKeyService _keyService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<FederationNodeClient> _logger;

    public FederationNodeClient(
        FederationNodeService nodeService,
        FederationKeyService keyService,
        IHttpClientFactory httpFactory,
        ILogger<FederationNodeClient> logger)
    {
        _nodeService = nodeService;
        _keyService = keyService;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// Reserve a fresh hub-minted id for a new planet on this node.
    /// </summary>
    public Task<TaskResult<FederatedPlanetStubResponse>> ReservePlanetAsync(FederatedPlanetStubRequest request) =>
        SendAsync<FederatedPlanetStubResponse>(HttpMethod.Post, "api/federation/planets", request);

    /// <summary>
    /// Register a stub at a specific id — a migrated or edge-generated planet.
    /// </summary>
    public Task<TaskResult<FederatedPlanetStubResponse>> AdoptPlanetAsync(long id, FederatedPlanetStubRequest request) =>
        SendAsync<FederatedPlanetStubResponse>(HttpMethod.Post, $"api/federation/planets/{id}/adopt", request);

    /// <summary>
    /// Update an existing stub (name, member count, discoverability, ...).
    /// </summary>
    public Task<TaskResult<FederatedPlanetStubResponse>> UpsertPlanetAsync(long id, FederatedPlanetStubRequest request) =>
        SendAsync<FederatedPlanetStubResponse>(HttpMethod.Put, $"api/federation/planets/{id}", request);

    public async Task<TaskResult> DeletePlanetAsync(long id)
    {
        var result = await SendAsync<object>(HttpMethod.Delete, $"api/federation/planets/{id}", null);
        return result.Success ? TaskResult.SuccessResult : TaskResult.FromFailure(result.Message);
    }

    private async Task<TaskResult<T>> SendAsync<T>(HttpMethod method, string path, object body)
    {
        if (!FederationNodeService.NodeEnabled)
            return TaskResult<T>.FromFailure("This instance is not a community node.");

        var token = await _nodeService.MintS2STokenAsync(_keyService);
        if (token is null)
            return TaskResult<T>.FromFailure("Node signing key unavailable.");

        try
        {
            var client = _httpFactory.CreateClient("federation");
            var url = FederationConfig.Current.HubUrl.TrimEnd('/') + "/" + path;

            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add(FederationApi.NodeAuthHeader, token);
            if (body is not null)
                request.Content = JsonContent.Create(body);

            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Hub call {Method} {Path} failed: {Status} {Detail}", method, path, (int)response.StatusCode, detail);
                return TaskResult<T>.FromFailure($"Hub returned {(int)response.StatusCode}.");
            }

            if (typeof(T) == typeof(object))
                return TaskResult<T>.FromData(default!);

            var data = await response.Content.ReadFromJsonAsync<T>();
            return TaskResult<T>.FromData(data);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Hub call {Method} {Path} threw", method, path);
            return TaskResult<T>.FromFailure(e.Message);
        }
    }
}
