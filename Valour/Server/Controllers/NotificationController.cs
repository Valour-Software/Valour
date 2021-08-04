using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Shared;
using Valour.Shared.Notifications;

namespace Valour.Server.Controllers
{

    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */


    /// <summary>
    /// This controller is responsible for allowing authentification of users
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class NotificationController
    {
        /// <summary>
        /// Database context for controller
        /// </summary>
        private readonly ValourDB Context;

        // Dependency injection
        public NotificationController(ValourDB context)
        {
            this.Context = context;
        }

        public async Task<TaskResult> SubmitSubscription([FromBody] NotificationSubscription subscription, string token)
        {
            var auth = await ServerAuthToken.TryAuthorize(token, Context);

            if (auth == null)
            {
                return new TaskResult(false, "Could not authorize user.");
            }

            var user = await Context.Users.FindAsync(subscription.User_Id);

            if (auth.User_Id != user.Id)
            {
                return new TaskResult(false, "Mismatch between auth and requested user notification hook.");
            }

            if (user == null)
            {
                return new TaskResult(false, $"Could not find user with Id {subscription.User_Id}");
            }

            if (string.IsNullOrWhiteSpace(subscription.Endpoint)
                || string.IsNullOrWhiteSpace(subscription.Auth)
                || string.IsNullOrWhiteSpace(subscription.Not_Key))
            {
                return new TaskResult(false, "Subscription data is incomplete.");
            }

            var old_sub = await Context.NotificationSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == subscription.Endpoint);

            if (old_sub != null)
            {
                if (old_sub.Auth == subscription.Auth && old_sub.Not_Key == subscription.Not_Key)
                {
                    return new TaskResult(false, "There is already a subscription for this endpoint.");
                }


                // Update old subscription
                old_sub.Auth = subscription.Auth;
                old_sub.Not_Key = subscription.Not_Key;

                Context.Update(old_sub);
                await Context.SaveChangesAsync();

                return new TaskResult(true, "Updated subscription.");
            }

            subscription.Id = IdManager.Generate();

            await Context.NotificationSubscriptions.AddAsync(subscription);
            await Context.SaveChangesAsync();

            return new TaskResult(true, "Subscription was accepted.");
        } 
    }
}
