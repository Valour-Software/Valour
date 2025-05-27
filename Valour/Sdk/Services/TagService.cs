using Valour.Sdk.Client;
using Valour.Shared;

namespace Valour.Sdk.Services;

public class TagService : ServiceBase
{
    private readonly ValourClient _client;
    private readonly LogOptions _logOptions = new(
        "TagService",
        "#2211a3",
        "#a1233e",
        "#a39433"
    );

    public TagService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, _logOptions);
    }
    
    public async Task<TaskResult<List<Tag>>> FetchTagsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<Tag>>("api/tags");
        return !response.Success ? TaskResult<List<Tag>>.FromFailure(response.Message) : TaskResult<List<Tag>>.FromData(response.Data);
    }
    
    public async ValueTask<Tag> FetchTagByIdAsync(long id, bool skipCache = false)
    {
        if (!skipCache && _client.Cache.Tags.TryGet(id, out var cached))
            return cached;

        var response = await _client.PrimaryNode.GetJsonAsync<Tag>($"api/tags/{id}");
        
        return response.Success ? 
            response.Data : 
            null;
    }
    
    public async Task<TaskResult<Tag>> CreateTagAsync(Tag tag)
    {
        var response = await _client.PrimaryNode.PostAsyncWithResponse<Tag>("api/tags", tag);

        if (!response.Success)
            return TaskResult<Tag>.FromFailure(response.Message);
        
        var createdTag = response.Data;
        return TaskResult<Tag>.FromData(createdTag);
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