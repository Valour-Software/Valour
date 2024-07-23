using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Sdk.Client;

public static class StaffTools
{
    public static PagedReader<Report> GetReportQueryReader(ReportQueryModel model, int amount = 50)
    {
        return new PagedReader<Report>("api/staff/reports", amount, postData: model);
    }
    
    public static PagedReader<User> GetUserQueryReader(UserQueryModel model, int amount = 50)
    {
        return new PagedReader<User>("api/users/query", amount, postData: model);
    }
    
    public static async Task<TaskResult> SetUserDisabledAsync(long userId, bool value)
    {
        var request = new DisableUserRequest()
        {
            UserId = userId,
            Value = value
        };
        
        return await ValourClient.PrimaryNode.PostAsync($"api/staff/disable", request);
    }
    
    public static async Task<TaskResult> DeleteUserAsync(long userId)
    {
        var request = new DeleteUserRequest()
        {
            UserId = userId
        };
        
        return await ValourClient.PrimaryNode.PostAsync($"api/staff/delete", request);
    }
}