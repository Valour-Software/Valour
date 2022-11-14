using Microsoft.AspNetCore.SignalR;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Messages;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Shared.Authorization;
using Valour.Api.Items.Messages.Embeds.Items;
using Valour.Api.Items.Messages.Embeds;
using System.Collections.Concurrent;
using Valour.Shared;
using IdGen;
using Newtonsoft.Json.Linq;
using Valour.Server.Database.Items.Channels;
using Valour.Server.Database.Nodes;
using Valour.Server.API;
using Valour.Shared.Channels;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database
{
    public class PlanetHub : Hub
    {
        public const string HubUrl = "/planethub";

        private readonly ValourDB _db;

        public PlanetHub(ValourDB db)
        {
            this._db = db;
        }



        /// <summary>
        /// We can store the authentication tokens for clients after they connect to the hub, and use them as long as the connection lasts
        /// </summary>
        public static ConcurrentDictionary<string, AuthToken> ConnectionIdentities = new ConcurrentDictionary<string, AuthToken>();

        // Map of groups to joined identities 
        public static ConcurrentDictionary<string, List<string>> GroupConnections = new ConcurrentDictionary<string, List<string>>();

        // Map of groups to user ids
        public static ConcurrentDictionary<string, List<long>> GroupUserIds = new ConcurrentDictionary<string, List<long>>();

        // Map of connection to joined groups
        public static ConcurrentDictionary<string, List<string>> ConnectionGroups = new ConcurrentDictionary<string, List<string>>();

        // Map of user id to joined groups
        public static ConcurrentDictionary<long, List<string>> UserIdGroups = new ConcurrentDictionary<long, List<string>>();

        // Map of connection id to user id
        // This specifically stores primary user connections
        public static ConcurrentDictionary<string, PrimaryNodeConnection> PrimaryConnections = new ConcurrentDictionary<string, PrimaryNodeConnection>();

        public static IHubContext<PlanetHub> Current;

        #region Connection Tracking
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            await RemovePrimaryConnection(Context.ConnectionId);

            RemoveAllMemberships();

            await base.OnDisconnectedAsync(exception);
        }

        public AuthToken GetToken(string connectionId)
        {
            if (!ConnectionIdentities.ContainsKey(connectionId))
                return null;

            return ConnectionIdentities[connectionId];
        }

        public async Task<TaskResult> Authorize(string token)
        {
            // Authenticate user
            AuthToken authToken = await AuthToken.TryAuthorize(token, _db);

            if (authToken is null)
                return new TaskResult(false, "Failed to authenticate connection.");

            ConnectionIdentities[Context.ConnectionId] = authToken;

            return new TaskResult(true, "Authenticated with SignalR hub successfully.");
        }

        public void TrackGroupMembership(string groupId)
        {
            // Create connection group list if it doesn't exist
            if (!ConnectionGroups.ContainsKey(Context.ConnectionId))
                ConnectionGroups[Context.ConnectionId] = new();

            // Add group to connection
            ConnectionGroups[Context.ConnectionId].Add(groupId);

            // Create group connection list if it doesn't exist
            if (!GroupConnections.ContainsKey(groupId))
                GroupConnections[groupId] = new();

            // Add connection to group
            GroupConnections[groupId].Add(Context.ConnectionId);

            // User part

            // Get identity of the connection
            ConnectionIdentities.TryGetValue(Context.ConnectionId, out var token);
            if (token is null)
                return;

            var userId = token.UserId;

            // Create user group list if it doesn't exist
            if (!UserIdGroups.ContainsKey(userId))
                UserIdGroups[userId] = new();

            // Add group to user
            UserIdGroups[userId].Add(groupId);

            // Create group user list if it doesn't exist
            if (!GroupUserIds.ContainsKey(groupId))
                GroupUserIds[groupId] = new();

            // Add user to group if not already
            //if (!GroupUserIds[groupId].Contains(userId)) This is actually bad
                GroupUserIds[groupId].Add(userId);
        }

        public void UntrackGroupMembership(string groupId)
        {
            // Remove connection from group
            GroupConnections.TryGetValue(groupId, out var connections);
            if (connections is not null)
                connections.Remove(Context.ConnectionId);

            // Remove group from connection
            ConnectionGroups.TryGetValue(Context.ConnectionId, out var groups);
            if (groups is not null)
                groups.Remove(groupId);

            // Get connection identity
            ConnectionIdentities.TryGetValue(Context.ConnectionId, out var authToken);
            if (authToken is null)
                return;

            var userId = authToken.UserId;

            // Remove userid from group
            GroupUserIds.TryGetValue(groupId, out var userIds);
            if (userIds is not null)
                userIds.Remove(userId);

            // Remove group id from user
            UserIdGroups.TryGetValue(userId, out var groupIds);
            if (groupIds is not null)
                groupIds.Remove(groupId);
        }

        public void RemoveAllMemberships()
        {
            // Get all groups for connection
            ConnectionGroups.TryGetValue(Context.ConnectionId, out var groups);
            if (groups is null)
                return;

            // Clear each group
            foreach (var group in groups.ToArray())
                UntrackGroupMembership(group);

            // Remove connection key from groups
            ConnectionGroups.Remove(Context.ConnectionId, out _);

            // Remove connection identity
            //ConnectionIdentities.Remove(Context.ConnectionId, out _);
        }

        public async Task AddPrimaryConnection(string connectionId, long userId)
        {
            var conn = new PrimaryNodeConnection()
            {
                ConnectionId = connectionId,
                UserId = userId,
                OpenTime = DateTime.UtcNow,
                NodeId = NodeAPI.Node.Name
            };

            // Add to collection
            var added = PrimaryConnections.TryAdd(connectionId, conn);

            if (added)
            {
                // Add to database
                await _db.PrimaryNodeConnections.AddAsync(conn);
                await _db.SaveChangesAsync();
            }
        }

        public async Task RemovePrimaryConnection(string connectionId)
        {
            // Remove any existing connection from collection
            var removed = PrimaryConnections.Remove(connectionId, out var conn);

            if (removed)
            {
                // Remove from database
                _db.PrimaryNodeConnections.Remove(conn);

                await _db.SaveChangesAsync();
            }
        }

        #endregion

        /// <summary>
        /// Primary node connection for user-wide events
        /// </summary>
        public async Task<TaskResult> JoinUser()
        {
            var authToken = GetToken(Context.ConnectionId);
            if (authToken == null) return new TaskResult(false, "Failed to connect to User: SignalR was not authenticated.");

            var groupId = $"u-{authToken.UserId}";

            TrackGroupMembership(groupId);

            await AddPrimaryConnection(Context.ConnectionId, authToken.UserId);

            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);

            using ValourDB db = new ValourDB(ValourDB.DBOptions);

            return new TaskResult(true, "Connected to user " + groupId);
        }

        public async Task LeaveUser()
        {
            var authToken = GetToken(Context.ConnectionId);
            if (authToken == null) return;

            var groupId = $"u-{authToken.UserId}";

            UntrackGroupMembership(groupId);

            await RemovePrimaryConnection(Context.ConnectionId);

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
        }

        public async Task<TaskResult> JoinPlanet(long planetId)
        {
            using ValourDB db = new ValourDB(ValourDB.DBOptions);
            
            var authToken = GetToken(Context.ConnectionId);
            if (authToken == null) return new TaskResult(false, "Failed to connect to Planet: SignalR was not authenticated.");

            PlanetMember member = await db.PlanetMembers.FirstOrDefaultAsync(
                x => x.UserId == authToken.UserId && x.PlanetId == planetId);

            // If the user is not a member, cancel
            if (member == null)
            {
                return new TaskResult(false, "Failed to connect to Planet: You are not a member.");
            }
            
            var groupId = $"p-{planetId}";
            TrackGroupMembership(groupId);

            // Add to planet group
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);


            return new TaskResult(true, "Connected to planet " + planetId);
        }

        public async Task LeavePlanet(long planetId) {
            var groupId = $"p-{planetId}";
            UntrackGroupMembership(groupId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
        }


        public async Task<TaskResult> JoinChannel(long channelId)
        {

            using ValourDB db = new(ValourDB.DBOptions);

            var authToken = GetToken(Context.ConnectionId);
            if (authToken == null) return new TaskResult(false, "Failed to connect to Channel: SignalR was not authenticated.");

            // Grab channel
            var channel = await db.PlanetChatChannels.FindAsync(channelId);
            if (channel is null)
                return new TaskResult(false, "Failed to connect to Channel: Channel was not found.");

            

            PlanetMember member = await db.PlanetMembers.FirstOrDefaultAsync(
                x => x.UserId == authToken.UserId && x.PlanetId == channel.PlanetId);

            if (!await channel.HasPermissionAsync(member, ChatChannelPermissions.ViewMessages, db))
                return new TaskResult(false, "Failed to connect to Channel: Member lacks view permissions.");

            var groupId = $"c-{channelId}";

            TrackGroupMembership(groupId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);

            // These are always the last messages so we always update user state for these
            var channelState = await db.UserChannelStates.FirstOrDefaultAsync(x => x.UserId == authToken.UserId && x.ChannelId == channel.Id);
            if (channelState != null)
            {
                channelState.LastViewedState = channel.GetCurrentState();
                NotifyUserChannelStateUpdate(authToken.UserId, channelState);
                await db.SaveChangesAsync();
            }

            return new TaskResult(true, "Connected to channel " + channelId);
        }

        public async Task LeaveChannel(long channelId) {
            var groupId = $"c-{channelId}";
            UntrackGroupMembership(groupId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
        }


        public async Task JoinInteractionGroup(long planetId)
        {
            using (ValourDB db = new(ValourDB.DBOptions))
            {

                var authToken = GetToken(Context.ConnectionId);
                if (authToken == null) return;

                PlanetMember member = await db.PlanetMembers.FirstOrDefaultAsync(
                    x => x.UserId == authToken.UserId && x.PlanetId == planetId);

                // If the user is not a member, cancel
                if (member == null)
                {
                    return;
                }
            }

            // Add to planet group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"i-{planetId}");
        }

        public static async void RelayMessage(PlanetMessage message)
        {
            var groupId = $"c-{message.ChannelId}";

            // Group we are sending messages to
            var group = Current.Clients.Group(groupId);

            if (GroupConnections.ContainsKey(groupId)) {
                // All of the connections to this group
                var viewingIds = GroupUserIds[groupId];

                using ValourDB db = new(ValourDB.DBOptions);

                await db.Database.ExecuteSqlRawAsync("CALL batch_user_channel_state_update({0}, {1});", viewingIds, message.ChannelId);
            }

            await group.SendAsync("Relay", message);
        }

        public static async void RelayDirectMessage(DirectMessage message, long targetUserId)
        {
            var groupId = $"u-{targetUserId}";

            // Group we are sending messages to
            var group = Current.Clients.Group(groupId);

            await group.SendAsync("RelayDirect", message);
        }

        public static async void NotifyUserChannelStateUpdate(long userId, UserChannelState state) =>
            await Current.Clients.Group($"u-{userId}").SendAsync("UserChannelState-Update", state);

        public static async void NotifyPlanetItemChange(IPlanetItem item, int flags = 0) =>
            await Current.Clients.Group($"p-{item.PlanetId}").SendAsync($"{item.GetType().Name}-Update", item, flags);

        public static async void NotifyPlanetItemDelete(IPlanetItem item) =>
            await Current.Clients.Group($"p-{item.PlanetId}").SendAsync($"{item.GetType().Name}-Delete", item);

        public static async void NotifyPlanetChange(Planet item, int flags = 0) =>
            await Current.Clients.Group($"p-{item.Id}").SendAsync($"{item.GetType().Name}-Update", item, flags);

        public static async void NotifyPlanetDelete(Planet item) =>
            await Current.Clients.Group($"p-{item.Id}").SendAsync($"{item.GetType().Name}-Delete", item);

        public async Task LeaveInteractionGroup(long planetId) =>
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"i-{planetId}");

        public static async void NotifyInteractionEvent(EmbedInteractionEvent interaction) =>
            await Current.Clients.Group($"i-{interaction.PlanetId}").SendAsync("InteractionEvent", interaction);

        public static async void NotifyMessageDeletion(PlanetMessage message) =>
            await Current.Clients.Group($"c-{message.ChannelId}").SendAsync("DeleteMessage", message);

        public static async void NotifyDirectMessageDeletion(DirectMessage message, long targetUserId) =>
            await Current.Clients.Group($"u-{targetUserId}").SendAsync("DeleteMessage", message);

        public static async void NotifyPersonalEmbedUpdateEvent(PersonalEmbedUpdate u) =>
            await Current.Clients.Group($"u-{u.TargetUserId}").SendAsync("Personal-Embed-Update", u);

        public static async Task NotifyUserChange(User user, ValourDB db, int flags = 0)
        {
            var members = await db.PlanetMembers.Where(x => x.UserId == user.Id).ToListAsync();

            foreach (var m in members)
            {
                // TODO: This will not work with node scaling
                await Current.Clients.Group($"p-{m.PlanetId}").SendAsync("User-Update", user, flags);
            }
        }

        public static async void NotifyUserDelete(User user, ValourDB db)
        {
            var members = db.PlanetMembers.Where(x => x.UserId == user.Id);

            foreach (var m in members)
            {
                await Current.Clients.Group($"p-{m.PlanetId}").SendAsync("User-Delete", user);
            }
        }

        public string Ping() => "Pong";

        public static async Task UpdateChannelsWatching()
        {
            foreach (var pair in GroupUserIds)
            {
                // Channel connections only
                if (!pair.Key.StartsWith('c'))
                    continue;
                
                // Send current active channel connection user ids
                await Current.Clients.Group(pair.Key).SendAsync("Channel-Watching-Update", new ChannelWatchingUpdate()
                {
                    ChannelId = long.Parse(pair.Key.Substring(2)),
                    UserIds = pair.Value.Distinct().ToList()
                });
            }
        }
    }
}
