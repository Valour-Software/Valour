using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Messages;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Shared.Items.Messages.Embeds;

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

        //public async Task JoinChannel()

        public static IHubContext<PlanetHub> Current;
        public async Task JoinPlanet(long planetId, string token)
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {

                // Authenticate user
                AuthToken authToken = await AuthToken.TryAuthorize(token, Context);

                if (authToken == null) return;

                PlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(
                    x => x.UserId == authToken.UserId && x.PlanetId == planetId);

                // If the user is not a member, cancel
                if (member == null)
                {
                    return;
                }
            }

            // Add to planet group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"p-{planetId}");
        }

        public async Task LeavePlanet(long planetId) =>
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"p-{planetId}");


        public async Task JoinChannel(long channelId, string token)
        {

            // TODO: Check if user has permission to view channel
            await Groups.AddToGroupAsync(Context.ConnectionId, $"c-{channelId}");
        }

        public async Task LeaveChannel(long channelId) =>
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"c-{channelId}");


        public async Task JoinInteractionGroup(long planetId, string token)
        {
            using (ValourDB Context = new(ValourDB.DBOptions))
            {

                // Authenticate user
                AuthToken authToken = await AuthToken.TryAuthorize(token, Context);

                if (authToken == null) return;

                PlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(
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

        public static async void NotifyPlanetItemChange(PlanetItem item, int flags = 0) =>
            await Current.Clients.Group($"p-{item.PlanetId}").SendAsync($"{item.GetType().Name}-Update", item, flags);

        public static async void NotifyPlanetItemDelete(PlanetItem item) =>
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

        public static async void NotifyUserChange(User user, ValourDB db, int flags = 0)
        {
            var members = db.PlanetMembers.Where(x => x.UserId == user.Id);

            foreach (var m in members)
            {
                // Not awaited on purpose
                //var t = Task.Run(async () => {
                //Console.WriteLine(JsonSerializer.Serialize(user));

                await Current.Clients.Group($"p-{m.PlanetId}").SendAsync("UserUpdate", user, flags);
                //await Current.Clients.Group($"p-{m.PlanetId}").SendAsync("ChannelUpdate", new PlanetChatChannel(), flags);
                //});
            }
        }

        public static async void NotifyUserDelete(User user, ValourDB db)
        {
            var members = db.PlanetMembers.Where(x => x.UserId == user.Id);

            foreach (var m in members)
            {
                await Current.Clients.Group($"p-{m.PlanetId}").SendAsync("UserDeletion", user);
            }
        }
    }
}
