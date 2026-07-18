using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// Bring-your-own-storage API calls, hung off the planet model.
/// </summary>
public static class PlanetStorageExtensions
{
    public static Task<TaskResult<PlanetStorageInfo>> FetchStorageInfoAsync(this Planet planet) =>
        planet.Node.GetJsonAsync<PlanetStorageInfo>($"api/planets/{planet.Id}/storage", allow404: true);

    public static Task<TaskResult<PlanetStorageInfo>> SetStorageConfigAsync(this Planet planet, PlanetStorageConfigRequest request) =>
        planet.Node.PutAsyncWithResponse<PlanetStorageInfo>($"api/planets/{planet.Id}/storage", request);

    public static Task<TaskResult> ClearStorageConfigAsync(this Planet planet) =>
        planet.Node.DeleteAsync($"api/planets/{planet.Id}/storage");

    public static Task<TaskResult<TaskResult>> ProbeStorageAsync(this Planet planet) =>
        planet.Node.PostAsyncWithResponse<TaskResult>($"api/planets/{planet.Id}/storage/probe");

    public static Task<TaskResult<PlanetMediaUploadGrant>> CreateMediaUploadGrantAsync(
        this Planet planet, PlanetMediaUploadRequest request) =>
        planet.Node.PostAsyncWithResponse<PlanetMediaUploadGrant>($"api/planets/{planet.Id}/storage/grants", request);
}
