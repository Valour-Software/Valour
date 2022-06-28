using System.Text.Json.Serialization;
using Valour.Api.Client;
using Valour.Api.Items.Authorization;
using Valour.Api.Items.Messages;
using Valour.Api.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Api.Items.Planets.Channels;


/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public class PlanetChatChannel : PlanetChatChannelBase, IPlanetChannel, ISyncedItem<PlanetChatChannel>, INodeSpecific
{
    #region Synced Item System

    /// <summary>
    /// Ran when this item is updated
    /// </summary>
    public event Func<int, Task> OnUpdated;

    /// <summary>
    /// Ran when this item is deleted
    /// </summary>
    public event Func<Task> OnDeleted;

    /// <summary>
    /// Run when any of this item type is updated
    /// </summary>
    public static event Func<PlanetChatChannel, int, Task> OnAnyUpdated;

    /// <summary>
    /// Run when any of this item type is deleted
    /// </summary>
    public static event Func<PlanetChatChannel, Task> OnAnyDeleted;

    public async Task InvokeAnyUpdated(PlanetChatChannel updated, int flags)
    {
        if (OnAnyUpdated != null)
            await OnAnyUpdated?.Invoke(updated, flags);
    }

    public async Task InvokeAnyDeleted(PlanetChatChannel deleted)
    {
        if (OnAnyDeleted != null)
            await OnAnyDeleted?.Invoke(deleted);
    }

    public async Task InvokeUpdated(int flags)
    {
        await OnUpdate(flags);

        if (OnUpdated != null)
            await OnUpdated?.Invoke(flags);
    }

    public async Task InvokeDeleted()
    {
        if (OnDeleted != null)
            await OnDeleted?.Invoke();
    }

    public async Task OnUpdate(int flags)
    {

    }

    #endregion

    /// The item type of this item
    /// </summary>
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.ChatChannel;

    /// <summary>
    /// Returns the channel for the given id
    /// </summary>
    public static async Task<PlanetChatChannel> FindAsync(ulong id, bool force_refresh = false)
    {
        if (!force_refresh)
        {
            var cached = ValourCache.Get<PlanetChatChannel>(id);
            if (cached is not null)
                return cached;
        }
            
        var channel = await ValourClient.GetJsonAsync<PlanetChatChannel>($"api/channel/{id}");

        if (channel is not null)
            await ValourCache.Put(id, channel);

        return channel;
    }

    /// <summary>
    /// Returns the name of the item type
    /// </summary>
    public string GetItemTypeName() => "Chat Channel";

    /// <summary>
    /// Attempts to delete the channel
    /// </summary>
    public async Task<TaskResult> DeleteAsync() => 
        await ValourClient.DeleteAsync($"api/channel/{Id}");

    /// <summary>
    /// Returns the planet this channel belongs to
    /// </summary>
    public async Task<Planet> GetPlanetAsync() => 
        await Planet.FindAsync(PlanetId);

    /// <summary>
    /// Sets the name of this channel
    /// </summary>
    public async Task<TaskResult> SetNameAsync(string name) =>
        await ValourClient.PutAsync($"api/channel/{Id}/name", name);

    /// <summary>
    /// Sets the description of this channel
    /// </summary>
    public async Task<TaskResult> SetDescriptionAsync(string desc) => 
        await ValourClient.PutAsync($"api/channel/{Id}/description", desc);

    /// <summary>
    /// Sets if the permissions should be inherited from the parent category
    /// </summary>
    public async Task<TaskResult> SetPermInheritModeAsync(bool value) => 
        await ValourClient.PutAsync($"api/channel/{Id}/inherits_perms", value);

    /// <summary>
    /// Sets the parent category id of the channel
    /// </summary>
    public async Task<TaskResult> SetParentIdAsync(ulong? id) => 
        await ValourClient.PutAsync($"api/channel/{Id}/parentId", id);


    /// <summary>
    /// Returns the permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetPermissionsNodeAsync(ulong roleId, bool force_refresh = false) =>
        await GetChannelPermissionsNodeAsync(roleId, force_refresh);

    /// <summary>
    /// Returns the channel permissions node for the given role id
    /// </summary>
    public async Task<PermissionsNode> GetChannelPermissionsNodeAsync(ulong roleId, bool force_refresh = false) =>
        await PermissionsNode.FindAsync(Id, roleId, ItemType.ChatChannel, force_refresh);

    /// <summary>
    /// Returns the current total permissions for this channel for a member.
    /// This result is NOT SYNCED, since it flattens several nodes into one!
    /// </summary>
    public async Task<PermissionsNode> GetMemberPermissionsAsync(ulong memberId, bool force_refresh = false)
    {
        var member = await PlanetMember.FindAsync(memberId);
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
            TargetType = ItemType
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
            var node = await GetChannelPermissionsNodeAsync(role.Id, force_refresh);

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

    public async Task<bool> HasPermissionAsync(ulong memberId, ChatChannelPermission perm) =>
        await ValourClient.GetJsonAsync<bool>($"api/channel/{Id}/hasperm/{memberId}/{perm.Value}");

    /// <summary>
    /// Returns the last (count) messages starting at (index)
    /// </summary>
    public async Task<List<PlanetMessage>> GetMessagesAsync(ulong index = ulong.MaxValue, int count = 10) =>
        await ValourClient.GetJsonAsync<List<PlanetMessage>>($"api/channel/{Id}/messages?index={index}&count={count}");

    /// <summary>
    /// Returns the last (count) messages
    /// </summary>
    public async Task<List<PlanetMessage>> GetLastMessagesAsync(int count = 10) =>
        await ValourClient.GetJsonAsync<List<PlanetMessage>>($"api/channel/{Id}/messages?count={count}");
}

