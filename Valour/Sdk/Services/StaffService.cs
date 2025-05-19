using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Economy;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Sdk.Services;

public class StaffService : ServiceBase
{
    private static readonly LogOptions LogOptions = new (
        "StaffService",
        "#036bfc",
        "#fc0356",
        "#fc8403"
    );
    
    private readonly ValourClient _client;
    
    public StaffService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }
    
    public async Task<TaskResult> SetUserDisabledAsync(long userId, bool value)
    {
        var request = new DisableUserRequest()
        {
            UserId = userId,
            Value = value
        };
        
        return await _client.PrimaryNode.PostAsync($"api/staff/disable", request);
    }
    
    public async Task<TaskResult> DeleteUserAsync(long userId)
    {
        var request = new DeleteUserRequest()
        {
            UserId = userId
        };
        
        return await _client.PrimaryNode.PostAsync($"api/staff/delete", request);
    }

    public async Task<Message> GetMessageAsync(long messageId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<Message>($"api/staff/messages/{messageId}");
        return result.Data;
    }
}