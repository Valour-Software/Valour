using System;
using Valour.Api.Client;
using Valour.Api.Nodes;
using Valour.Api.Requests;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Api.Models;


/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetChatChannel : PlanetChannel, ISharedPlanetChatChannel, IChatChannel
{
    #region IPlanetItem implementation

    public override string BaseRoute =>
            $"api/chatchannels";

    #endregion

    /// <summary>
    /// The total number of messages sent in this channel
    /// </summary>
    public long MessageCount { get; set; }

    /// <summary>
    /// Returns the name of the item type
    /// </summary>
    public override string GetHumanReadableName() => "Chat Channel";

    public override PermChannelType PermType => PermChannelType.PlanetChatChannel;

    public override async Task Open() =>
        await ValourClient.OpenPlanetChannel(this);

    public override async Task Close() =>
        await ValourClient.ClosePlanetChannel(this);

    public bool DetermineUnread()
    {
        if (ValourClient.OpenPlanetChannels.Any(x => x.Id == Id))
            return false;

        ValourClient.ChannelStateTimes.TryGetValue(Id, out var lastRead);
        if (lastRead is null)
            lastRead = DateTime.MaxValue;

        return lastRead < TimeLastActive;
    }

    /// <summary>
    /// Returns the current total permissions for this channel for a member.
    /// This result is NOT SYNCED, since it flattens several nodes into one!
    /// </summary>
    public async ValueTask<PermissionsNode> GetMemberPermissionsAsync(long memberId, long planetId, bool force_refresh = false)
    {
        var member = await PlanetMember.FindAsync(memberId, planetId);

        var roles = await member.GetRolesAsync();

        // Start with no permissions
        var dummy_node = new PermissionsNode()
        {
            // Full, since values should either be yes or no
            Mask = Permission.FULL_CONTROL,
            // Default to no permission
            Code = 0x0,

            PlanetId = PlanetId,
            TargetId = Id,
            TargetType = PermChannelType.PlanetChatChannel
        };

        var planet = await GetPlanetAsync();

        // Easy cheat for owner
        if (planet.OwnerId == member.UserId)
        {
            dummy_node.Code = Permission.FULL_CONTROL;
            return dummy_node;
        }

        // Should be in order of most power -> least,
        // so we reverse it here
        for (int i = roles.Count - 1; i >= 0; i--)
        {
            var role = roles[i];
            PermissionsNode node;
            // If true, we grab the parent's permission node
            if (InheritsPerms)
                node = await (await GetParentAsync()).GetPermNodeAsync(role.Id, PermChannelType.PlanetChatChannel, force_refresh);
            else
                node = await GetPermNodeAsync(role.Id, PermChannelType.PlanetChatChannel, force_refresh);

            if (node is null)
                continue;

            //Console.WriteLine($"{role.Name}: {node.Mask} {node.Code}");

            foreach (var perm in ChatChannelPermissions.Permissions)
            {
                var val = node.GetPermissionState(perm);

                // Change nothing if undefined. Otherwise overwrite.
                // Since most important nodes come last, we will end with correct perms.
                if (val == PermissionState.True)
                {
                    dummy_node.SetPermission(perm, PermissionState.True);
                }
                else if (val == PermissionState.False)
                {
                    dummy_node.SetPermission(perm, PermissionState.False);
                }
            }
        }

        return dummy_node;
    }

    /// <summary>
    /// Returns the item for the given id
    /// </summary>
    public static async ValueTask<PlanetChatChannel> FindAsync(long id, long planetId, bool refresh = false)
    {
        if (!refresh)
        {
            var cached = ValourCache.Get<PlanetChatChannel>(id);
            if (cached is not null)
                return cached;
        }

        var node = await NodeManager.GetNodeForPlanetAsync(planetId);
        var item = (await node.GetJsonAsync<PlanetChatChannel>($"api/chatchannels/{id}")).Data;

        if (item is not null)
            await item.AddToCache();

        return item;
    }

    public static async Task<TaskResult<PlanetChatChannel>> CreateWithDetails(CreatePlanetChatChannelRequest request)
    {
        var node = await NodeManager.GetNodeForPlanetAsync(request.Channel.PlanetId);
        return await node.PostAsyncWithResponse<PlanetChatChannel>($"{request.Channel.BaseRoute}/detailed", request);
    }

    public async Task<bool> HasPermissionAsync(long memberId, ChatChannelPermission perm) =>
        (await Node.GetJsonAsync<bool>($"{IdRoute}/checkperm/{memberId}/{perm.Value}")).Data;

    /// <summary>
    /// Returns the last (count) messages starting at (index)
    /// </summary>
    public async Task<List<PlanetMessage>> GetMessagesAsync(long index = long.MaxValue, int count = 10) =>
        (await Node.GetJsonAsync<List<PlanetMessage>>($"{IdRoute}/messages?index={index}&count={count}")).Data;

    /// <summary>
    /// Returns the last (count) messages
    /// </summary>
    public async Task<List<PlanetMessage>> GetLastMessagesAsync(int count = 10) =>
        (await Node.GetJsonAsync<List<PlanetMessage>>($"{IdRoute}/messages?count={count}")).Data;

    /// <summary>
    /// Returns the last (count) generic messages
    /// </summary>
    public async Task<List<Message>> GetLastMessagesGenericAsync(int count = 10) =>
        (await Node.GetJsonAsync<List<PlanetMessage>>($"{IdRoute}/messages?count={count}")).Data.Cast<Message>().ToList();

    /// <summary>
    /// Returns the last (count) generic messages starting at (index)
    /// </summary>
    public async Task<List<Message>> GetMessagesGenericAsync(long index = long.MaxValue, int count = 10) =>
        (await Node.GetJsonAsync<List<PlanetMessage>>($"{IdRoute}/messages?index={index}&count={count}")).Data.Cast<Message>().ToList();

    public async Task SendIsTyping()
    {
        await Node.PostAsync($"{IdRoute}/typing", null);
    }
}

