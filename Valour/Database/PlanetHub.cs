using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR;
using Valour.Database.Items.Planets;
using Valour.Database.Items.Planets.Members;
using Valour.Database.Items.Planets.Channels;
using Valour.Shared.Items.Messages.Embeds;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Users;
using System.Text.Json;
using Valour.Database.Items.Messages;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database
{
    public class PlanetHub : Hub
    {
        public const string HubUrl = "/planethub";

        //public async Task JoinChannel()

        public static IHubContext<PlanetHub> Current;
        public async Task JoinPlanet(ulong planet_id, string token)
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions)) {

                // Authenticate user
                AuthToken authToken = await AuthToken.TryAuthorize(token, Context);

                if (authToken == null) return;

                PlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(
                    x => x.User_Id == authToken.User_Id && x.Planet_Id == planet_id);

                // If the user is not a member, cancel
                if (member == null)
                {
                    return;
                }
            }

            // Add to planet group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"p-{planet_id}");
        }

        public async Task LeavePlanet(ulong planet_id) =>
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"p-{planet_id}");
        

        public async Task JoinChannel(ulong channel_id, string token)
        {

            // TODO: Check if user has permission to view channel
            await Groups.AddToGroupAsync(Context.ConnectionId, $"c-{channel_id}");
        }

        public async Task LeaveChannel(ulong channel_id) =>
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"c-{channel_id}");
        

        public async Task JoinInteractionGroup(ulong planet_id, string token)
        {
            using (ValourDB Context = new(ValourDB.DBOptions)) {

                // Authenticate user
                AuthToken authToken = await AuthToken.TryAuthorize(token, Context);

                if (authToken == null) return;

                PlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(
                    x => x.User_Id == authToken.User_Id && x.Planet_Id == planet_id);

                // If the user is not a member, cancel
                if (member == null)
                {
                    return;
                }
            }

            // Add to planet group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"i-{planet_id}");
        }

        public async Task LeaveInteractionGroup(ulong planet_id) =>
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"i-{planet_id}");

        public static async void NotifyMemberChange(PlanetMember member, int flags = 0) =>
            await Current.Clients.Group($"p-{member.Planet_Id}").SendAsync("MemberUpdate", member, flags);

        public static async void NotifyPlanetChange(Planet planet, int flags = 0) =>
            await Current.Clients.Group($"p-{planet.Id}").SendAsync("PlanetUpdate", planet, flags);

        public static async void NotifyInteractionEvent(EmbedInteractionEvent interaction) =>
            await Current.Clients.Group($"i-{interaction.Planet_Id}").SendAsync("InteractionEvent", interaction);

        public static async void NotifyRoleChange(PlanetRole role, int flags = 0) =>
            await Current.Clients.Group($"p-{role.Planet_Id}").SendAsync("RoleUpdate", role, flags);

        public static async Task NotifyCategoryDeletion(PlanetCategory category) =>
            await Current.Clients.Group($"p-{category.Planet_Id}").SendAsync("CategoryDeletion", category);

        public static async void NotifyRoleDeletion(PlanetRole role) =>
            await Current.Clients.Group($"p-{role.Planet_Id}").SendAsync("RoleDeletion", role);

        public static async Task NotifyChatChannelDeletion(PlanetChatChannel channel) =>
            await Current.Clients.Group($"p-{channel.Planet_Id}").SendAsync("ChannelDeletion", channel);

        public static async void NotifyChatChannelChange(PlanetChatChannel channel, int flags = 0) =>
            await Current.Clients.Group($"p-{channel.Planet_Id}").SendAsync("ChannelUpdate", channel, flags);

        public static async void NotifyCategoryChange(PlanetCategory category, int flags = 0) =>
            await Current.Clients.Group($"p-{category.Planet_Id}").SendAsync("CategoryUpdate", category, flags);

        public static async void NotifyMessageDeletion(PlanetMessage message) =>
            await Current.Clients.Group($"c-{message.Channel_Id}").SendAsync("DeleteMessage", message);

        public static async void NotifyUserChange(User user, ValourDB db, int flags = 0)
        {
            var members = db.PlanetMembers.Where(x => x.User_Id == user.Id);

            foreach (var m in members)
            {
                // Not awaited on purpose
                //var t = Task.Run(async () => {
                Console.WriteLine(JsonSerializer.Serialize(user));

                    await Current.Clients.Group($"p-{m.Planet_Id}").SendAsync("UserUpdate", user, flags);
                    //await Current.Clients.Group($"p-{m.Planet_Id}").SendAsync("ChannelUpdate", new PlanetChatChannel(), flags);
                //});
            }
        }

        public static async void NotifyUserDelete(User user, ValourDB db)
        {
            var members = db.PlanetMembers.Where(x => x.User_Id == user.Id);

            foreach (var m in members)
            {
                await Current.Clients.Group($"p-{m.Planet_Id}").SendAsync("UserDeletion", user);
            }
        }
    }
}
