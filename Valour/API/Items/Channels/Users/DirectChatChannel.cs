using Valour.Api.Client;
using Valour.Api.Items.Channels.Planets;
using Valour.Api.Items.Messages;
using Valour.Api.Items.Planets;
using Valour.Api.Nodes;
using Valour.Shared.Items.Channels.Users;

namespace Valour.Api.Items.Channels.Users;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/


public class DirectChatChannel : Channel, ISharedDirectChatChannel, IChatChannel
{
    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    public long UserOneId { get; set; }

    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    public long UserTwoId { get; set; }

    /// <summary>
    /// The total number of messages sent in this channel
    /// </summary>
    public long MessageCount { get; set; }

    public static async ValueTask<DirectChatChannel> FindAsyncByUser(long otherUserId, bool refresh = false)
    {
        if (!refresh)
        {
            long selfId = ValourClient.Self.Id;

            // We insert into the cache with lower-value id first to ensure a match
            // so we do the same to get it back
            long lowerId;
            long higherId;

            if (selfId > otherUserId)
            {
                higherId = selfId;
                lowerId = otherUserId;
            }
            else
            {
                higherId = otherUserId;
                lowerId = selfId;
            }

            var cached = ValourCache.Get<DirectChatChannel>((lowerId, higherId));
            if (cached is not null)
                return cached;
        }
        var item = (await ValourClient.PrimaryNode.GetJsonAsync<DirectChatChannel>($"api/{nameof(DirectChatChannel)}/byuser/{nameof(PlanetChatChannel)}/{otherUserId}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    public override async Task AddToCache()
    {
        // We insert into the cache with lower-value id first to ensure a match
        long lowerId;
        long higherId;

        if (UserOneId > UserTwoId)
        {
            higherId = UserOneId;
            lowerId = UserTwoId;
        }
        else
        {
            higherId = UserTwoId;
            lowerId = UserOneId;
        }

        // Add with key for users
        await ValourCache.Put((lowerId, higherId), this);

        // Add with key for lone id
        await ValourCache.Put(Id, this);
    }

    public override async Task Open()
    {

    }

    public override async Task Close()
    {
        
    }

    public async Task<List<Message>> GetLastMessagesGenericAsync(int count = 10)
    {
        throw new NotImplementedException();
    }

    public async Task<List<Message>> GetMessagesGenericAsync(long index = long.MaxValue, int count = 10)
    {
        throw new NotImplementedException();
    }
}
