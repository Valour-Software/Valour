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

    public async Task<TaskResult> VerifyUserAsync(string identifier)
    {
        var request = new VerifyUserRequest()
        {
            Identifier = identifier
        };

        return await _client.PrimaryNode.PostAsync("api/staff/users/verify", request);
    }

    public async Task<Message> GetMessageAsync(long messageId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<Message>($"api/staff/messages/{messageId}");
        return result.Data;
    }

    public async Task<Report> GetReportAsync(string reportId)
    {
        var result = await _client.PrimaryNode.GetJsonAsync<Report>($"api/staff/reports/{reportId}");
        return result.Data;
    }

    public async Task<TaskResult> ResolveReportAsync(string reportId, ReportResolution resolution, string staffNotes)
    {
        var request = new ResolveReportRequest()
        {
            ReportId = reportId,
            Resolution = resolution,
            StaffNotes = staffNotes
        };

        return await _client.PrimaryNode.PostAsync("api/staff/reports/resolve", request);
    }
}
