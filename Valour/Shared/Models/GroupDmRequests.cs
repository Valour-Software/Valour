namespace Valour.Shared.Models;

public class CreateGroupDmRequest
{
    public string Name { get; set; } = string.Empty;
    public List<long> UserIds { get; set; } = [];
}

public class AddGroupDmMembersRequest
{
    public List<long> UserIds { get; set; } = [];
}

public class UpdateGroupDmRequest
{
    public string Name { get; set; } = string.Empty;
}
