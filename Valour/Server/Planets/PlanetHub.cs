using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Categories;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Server.Roles;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using Valour.Shared.Messages;
using Valour.Shared.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Planets
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
                AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

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
                AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

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

        public static async void NotifyMemberChange(ServerPlanetMember member) =>
            await Current.Clients.Group($"p-{member.Planet_Id}").SendAsync("MemberUpdate", member);

        public static async void NotifyPlanetChange(ServerPlanet planet) =>
            await Current.Clients.Group($"p-{planet.Id}").SendAsync("PlanetUpdate", planet);

        public static async void NotifyInteractionEvent(InteractionEvent interaction) =>
            await Current.Clients.Group($"i-{interaction.Planet_Id}").SendAsync("InteractionEvent", interaction);

        public static async void NotifyRoleChange(ServerPlanetRole role) =>
            await Current.Clients.Group($"p-{role.Planet_Id}").SendAsync("RoleUpdate", role);

        public static async Task NotifyCategoryDeletion(ServerPlanetCategory category) =>
            await Current.Clients.Group($"p-{category.Planet_Id}").SendAsync("CategoryDeletion", category);

        public static async void NotifyRoleDeletion(ServerPlanetRole role) =>
            await Current.Clients.Group($"p-{role.Planet_Id}").SendAsync("RoleDeletion", role);

        public static async Task NotifyChatChannelDeletion(ServerPlanetChatChannel channel) =>
            await Current.Clients.Group($"p-{channel.Planet_Id}").SendAsync("ChatChannelDeletion", channel);

        public static async void NotifyChatChannelChange(ServerPlanetChatChannel channel) =>
            await Current.Clients.Group($"p-{channel.Planet_Id}").SendAsync("ChatChannelUpdate", channel);

        public static async void NotifyCategoryChange(ServerPlanetCategory category) =>
            await Current.Clients.Group($"p-{category.Planet_Id}").SendAsync("CategoryUpdate", category);
    }
}
