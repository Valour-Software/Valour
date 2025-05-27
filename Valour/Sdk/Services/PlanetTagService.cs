using Valour.Sdk.Client;
using Valour.Shared;

namespace Valour.Sdk.Services;

public class PlanetTagService : ServiceBase
{
    private readonly ValourClient _client;
    private readonly LogOptions _logOptions = new(
        "TagService",
        "#2211a3",
        "#a1233e",
        "#a39433"
    );

    public PlanetTagService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, _logOptions);
    }
    
    public async Task<TaskResult<List<PlanetTag>>> FetchTagsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<PlanetTag>>("api/tags");
        return !response.Success ? TaskResult<List<PlanetTag>>.FromFailure(response.Message) : TaskResult<List<PlanetTag>>.FromData(response.Data);
    }
    
    public async ValueTask<PlanetTag> FetchTagByIdAsync(long id, bool skipCache = false)
    {
        if (!skipCache && _client.Cache.Tags.TryGet(id, out var cached))
            return cached;

        var response = await _client.PrimaryNode.GetJsonAsync<PlanetTag>($"api/tags/{id}");
        
        return response.Success ? 
            response.Data : 
            null;
    }
    
    public async Task<TaskResult<PlanetTag>> CreateTagAsync(PlanetTag planetTag)
    {
        var response = await _client.PrimaryNode.PostAsyncWithResponse<PlanetTag>("api/tags", planetTag);

        if (!response.Success)
            return TaskResult<PlanetTag>.FromFailure(response.Message);
        
        var createdTag = response.Data;
        return TaskResult<PlanetTag>.FromData(createdTag);
    }

    public async Task<TaskResult> DeleteTagAsync(long tagId)
    {
        var response = await _client.PrimaryNode.DeleteAsync($"api/tags/{tagId}");
        
        if (response.Success)
        {
            _client.Cache.Tags.Remove(tagId);
        }
        
        return response.Success ? 
            TaskResult.SuccessResult : 
            TaskResult.FromFailure(response.Message);
    }
}