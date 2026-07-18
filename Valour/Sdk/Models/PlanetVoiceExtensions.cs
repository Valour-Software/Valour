using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// Bring-your-own-voice API calls, hung off the planet model.
/// </summary>
public static class PlanetVoiceExtensions
{
    public static Task<TaskResult<PlanetVoiceInfo>> FetchVoiceInfoAsync(this Planet planet) =>
        planet.Node.GetJsonAsync<PlanetVoiceInfo>($"api/planets/{planet.Id}/voice", allow404: true);

    public static Task<TaskResult<PlanetVoiceInfo>> SetVoiceConfigAsync(this Planet planet, PlanetVoiceConfigRequest request) =>
        planet.Node.PutAsyncWithResponse<PlanetVoiceInfo>($"api/planets/{planet.Id}/voice", request);

    public static Task<TaskResult> ClearVoiceConfigAsync(this Planet planet) =>
        planet.Node.DeleteAsync($"api/planets/{planet.Id}/voice");

    public static Task<TaskResult<TaskResult>> ProbeVoiceAsync(this Planet planet) =>
        planet.Node.PostAsyncWithResponse<TaskResult>($"api/planets/{planet.Id}/voice/probe");
}
