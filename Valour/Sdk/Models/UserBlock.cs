using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using System.Text.Json.Serialization;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class UserBlock : ClientModel<UserBlock, long>, ISharedUserBlock
{
    public override string BaseRoute => ISharedUserBlock.BaseRoute;

    [JsonIgnore]
    public override Node Node => Client?.AccountNode;

    public long UserId { get; set; }
    public long BlockedUserId { get; set; }
    public BlockType BlockType { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonConstructor]
    private UserBlock() : base() { }
    public UserBlock(ValourClient client) : base(client) { }

    public override UserBlock AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }

    public override UserBlock RemoveFromCache(bool skipEvents = false)
    {
        return null;
    }
}
