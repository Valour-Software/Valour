using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Api.Nodes;
using Valour.Shared.Models;

namespace Valour.Api.Models;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/


public class DirectChatChannel : Channel, ISharedDirectChatChannel, IChatChannel
{
    #region IPlanetModel implementation

    public override string BaseRoute =>
            $"api/directchatchannels";

    #endregion

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

    public static async ValueTask<DirectChatChannel> FindAsyncById(long id, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<DirectChatChannel>(id);
            if (cached is not null)
                return cached;
        }
        var item = (await ValourClient.PrimaryNode.GetJsonAsync<DirectChatChannel>($"api/directchatchannels/{id}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    public static async ValueTask<DirectChatChannel> FindAsyncByUser(long otherUserId, bool create = false, bool refresh = false)
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
        var item = (await ValourClient.PrimaryNode.GetJsonAsync<DirectChatChannel>($"api/directchatchannels/byuser/{otherUserId}?create={create}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    public override async Task AddToCache<T>(T item)
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

    /// <summary>
    /// Returns the last (count) messages starting at (index)
    /// </summary>
    public async Task<List<DirectMessage>> GetMessagesAsync(long index = long.MaxValue, int count = 10) =>
        (await ValourClient.PrimaryNode.GetJsonAsync<List<DirectMessage>>($"{IdRoute}/messages?index={index}&count={count}")).Data;

    /// <summary>
    /// Returns the last (count) messages
    /// </summary>
    public async Task<List<DirectMessage>> GetLastMessagesAsync(int count = 10) =>
        (await ValourClient.PrimaryNode.GetJsonAsync<List<DirectMessage>>($"{IdRoute}/messages?count={count}")).Data;

    /// <summary>
    /// Returns the last (count) generic messages
    /// </summary>
    public async Task<List<Message>> GetLastMessagesGenericAsync(int count = 10) =>
        (await ValourClient.PrimaryNode.GetJsonAsync<List<DirectMessage>>($"{IdRoute}/messages?count={count}")).Data.Cast<Message>().ToList();

    /// <summary>
    /// Returns the last (count) generic messages starting at (index)
    /// </summary>
    public async Task<List<Message>> GetMessagesGenericAsync(long index = long.MaxValue, int count = 10) =>
        (await ValourClient.PrimaryNode.GetJsonAsync<List<DirectMessage>>($"{IdRoute}/messages?index={index}&count={count}")).Data.Cast<Message>().ToList();

    // IsCurrentlyTyping is not supported for direct chat channels right now
    public Task SendIsTyping()
        => Task.CompletedTask;
}
