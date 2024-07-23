namespace Valour.Shared.Models;

public class UserQueryModel : QueryModel
{
    public override string GetApiUrl()
        => "api/users/query";

    public string UsernameAndTag { get; set; }
}