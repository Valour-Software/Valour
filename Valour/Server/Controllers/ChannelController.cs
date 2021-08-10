using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Messages;
using Valour.Shared.Oauth;
using Valour.Shared.Users;
using Valour.Shared.Planets;
using System.Text.RegularExpressions;
using System.Collections.Specialized;
using Valour.Server.Messages;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Workers;
using Valour.Server.Planets;
using AutoMapper;
using Valour.Server.Oauth;
using Valour.Server.Categories;
using WebPush;
using Valour.Server.MPS;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Controls all routes for channels
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class ChannelController
    {

        public static List<PlanetMessage> messageCache = new List<PlanetMessage>();

        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;

        private readonly IMapper Mapper;
        private readonly WebPushClient PushClient;

        // Dependency injection
        public ChannelController(ValourDB context, IMapper mapper, WebPushClient pushClient)
        {
            this.Context = context;
            this.Mapper = mapper;
            this.PushClient = pushClient;
        }
        
        public async Task<TaskResult<IEnumerable<ulong>>> GetChannelIdsAsync(ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<IEnumerable<ulong>>(false, "Please supply a valid authentication token.", null);
            }
            
            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, planet_id, Context);

            if (member == null)
            {
                return new TaskResult<IEnumerable<ulong>>(false, "You are not a member of this planet.", null);
            }

            var channels = Context.PlanetChatChannels.Where(c => c.Planet_Id == planet_id);

            List<ulong> result = new List<ulong>();

            foreach (var channel in channels)
            {
                if (await channel.HasPermission(member, ChatChannelPermissions.View, Context))
                {
                    result.Add(channel.Id);
                }
            }

            return new TaskResult<IEnumerable<ulong>>(true, "Successfully retrieved channels.", result);;
        }

        [HttpGet]
        public async Task<TaskResult<IEnumerable<PlanetChatChannel>>> GetPlanetChannelsAsync(ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<IEnumerable<PlanetChatChannel>>(false, "Please supply a valid authentication token.", null);
            }

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, planet_id, Context);

            if (member == null)
            {
                return new TaskResult<IEnumerable<PlanetChatChannel>>(false, "You are not a member of this planet.", null);
            }

            var channels = Context.PlanetChatChannels.Where(c => c.Planet_Id == planet_id);

            List<PlanetChatChannel> result = new List<PlanetChatChannel>();

            foreach (var channel in channels)
            {
                if (await channel.HasPermission(member, ChatChannelPermissions.View, Context))
                {
                    result.Add(channel);
                }
            }

            return new TaskResult<IEnumerable<PlanetChatChannel>>(true, "Successfully retrieved channels.", result);
        }

        [HttpPost]
        public async Task<TaskResult> PostMessage(PlanetMessage msg, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null || authToken.User_Id != msg.Author_Id)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == msg.Channel_Id);

            if (channel == null)
            {
                return new TaskResult(false, "Failed to post message: The given channel does not exist!");
            }

            ServerPlanetMember member = await Context.PlanetMembers.FindAsync(msg.Member_Id);

            if (member == null)
            {
                return new TaskResult(false, "Failed to post message: You are not in the planet!");
            }

            if (member.User_Id != msg.Author_Id)
            {
                return new TaskResult(false, "Failed to post message: User Id mismatch! Attempt at impersonation?");
            }

            if (!(await channel.HasPermission(member, ChatChannelPermissions.View, Context)))
            {
                return new TaskResult(false, "Failed to post message: You don't have permission to see this channel!");
            }

            if (!(await channel.HasPermission(member, ChatChannelPermissions.PostMessages, Context)))
            {
                return new TaskResult(false, "Failed to post message: You don't have permission to post here!");
            }

            //ClientMessage msg = JsonConvert.DeserializeObject<ClientMessage>(json);

            if (msg == null)
            {
                return new TaskResult(false, "Malformed message.");
            }

            // Stop people from sending insanely large messages

            if (msg.Embed_Data == null) {
                if (msg.Content.Length > 2048)
                {
                    return new TaskResult(false, "Message is longer than 2048 chars.");
                }
            }
            else {

                // also check if the user is a bot
                // sense only bots can send embeds
                // make sure only bots can post embeds

                User user = await member.GetUserAsync();

                if (!user.Bot) {
                    return new TaskResult(false, "Only bots may send embeds!");
                }

                if (msg.Embed_Data.Length > 65535)
                {
                    return new TaskResult(false, "Embed Data is longer than 65535 chars.");
                }
            }

            

            // Media proxy layer
            msg.Content = await MPSManager.HandleUrls(msg.Content);

            PlanetMessageWorker.AddToQueue(msg);

            StatWorker.IncreaseMessageCount();

            /*

            // Run this in another thread as quickly as possible
#pragma warning disable CS4014 
            Task.Run(async () =>
            {
                using (ValourDB db = new ValourDB(ValourDB.DBOptions))
                {
                    // Get all members of channel
                    var not_members = await channel.GetChannelMembersAsync(db);

                    // For each member get their subscribed devices and send notifications
                    //Parallel.ForEach(not_members, async mem =>
                    //{
                    foreach (var mem in not_members)
                    {
                        var not_subs = db.NotificationSubscriptions.Where(x => x.User_Id == mem.User_Id);

                        //Parallel.ForEach(not_subs, async sub =>
                        //{
                        foreach (var sub in not_subs)
                        {
                            var subscription = new PushSubscription(sub.Endpoint, sub.Not_Key, sub.Auth);

                            try
                            {
                                var details = VapidConfig.Current.GetDetails();

                                var payload = $"{{'title':'{member.Id}','message':'{msg.Content}'}}";

                                var pushClient = new WebPushClient();

                                await pushClient.SendNotificationAsync(subscription, payload, details);
                            }
                            catch (System.Exception e)
                            { 
                                Console.WriteLine(e.Message);
                            }
                        }
                        //});
                    }
                    //});
                }
            });

            var sub = await Context.NotificationSubscriptions.FirstOrDefaultAsync();

            var subscription = new PushSubscription(ep, ke, au);


            var details = new VapidDetails(VapidConfig.Current.Subject, VapidConfig.Current.PublicKey, VapidConfig.Current.PrivateKey);

            var payload = JsonSerializer.Serialize(
                new
                {
                    title = member.Nickname,
                    message = msg.Content
                }
            );

            var pushClient = new TestWebPushClient();

            await pushClient.SendNotificationAsync(subscription, payload, details);

            */

#pragma warning restore CS4014 


            return new TaskResult(true, "Added message to post queue.");
        }

        public async Task<TaskResult> SetName(ulong channel_id, string name, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult(false, $"Could not find channel {channel_id}");
            }

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You do not have planet channel management permissions.");
            }

            channel.Name = name;
            await Context.SaveChangesAsync();

            // Send channel refresh
            PlanetHub.NotifyChatChannelChange(channel);

            return new TaskResult(true, "Successfully changed channel name.");
        }

        public async Task<TaskResult> SetDescription(ulong channel_id, string description, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult(false, $"Could not find channel {channel_id}");
            }

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You do not have planet channel management permissions.");
            }

            channel.Description = description;
            await Context.SaveChangesAsync();

            // Send channel refresh
            PlanetHub.NotifyChatChannelChange(channel);

            return new TaskResult(true, "Successfully changed channel description.");
        }

        public async Task<TaskResult> SetPermissionInheritMode(ulong channel_id, bool value, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult(false, $"Could not find channel {channel_id}");
            }

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You do not have planet channel management permissions.");
            }

            channel.Inherits_Perms = value;
            await Context.SaveChangesAsync();

            // Send channel refresh
            PlanetHub.NotifyChatChannelChange(channel);

            return new TaskResult(true, $"Successfully set permission inheritance to {value}.");
        }
    }
}
