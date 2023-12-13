using Valour.Api.Client;
using Valour.Shared.Models;

namespace Valour.Api.Models;

public class Reaction : LiveModel, ISharedReaction
{
    public override string BaseRoute => $"api/reactions";

    /// <summary>
    /// The id of the message this reaction is on
    /// </summary>
    public long MessageId { get; set; }
    
    /// <summary>
    /// The user who added the reaction
    /// </summary>
    public long UserId { get; set; }

    public static async ValueTask<Reaction> FindAsync(long id, bool forceRefresh = false)
    {
        if (!forceRefresh)
        {
            var cached = ValourCache.Get<Reaction>(id);
            if (cached is not null)
                return cached;
        }

        var item = (await ValourClient.PrimaryNode.GetJsonAsync<Reaction>($"api/reactions/{id}")).Data;

        if (item is not null)
        {
            await ValourCache.Put(id, item);
        }

        return item;
    }
}