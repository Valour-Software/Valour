namespace Valour.Shared.Models;

public interface ISharedUserBlock : ISharedModel<long>
{
    const string BaseRoute = "api/userblocks";

    long UserId { get; set; }
    long BlockedUserId { get; set; }
    BlockType BlockType { get; set; }
    DateTime CreatedAt { get; set; }
}
