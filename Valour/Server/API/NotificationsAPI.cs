
namespace Valour.Server.API;
public class NotificationsAPI : BaseAPI
{
    /// <summary>
    /// Adds the routes for this API section
    /// </summary>
    public static void AddRoutes(WebApplication app)
    {
    }

    // Bother with this later

    /*
     public async Task<TaskResult> SubmitSubscription([FromBody] NotificationSubscription subscription, string token)
        {
            var auth = await ServerAuthToken.TryAuthorize(token, Context);

            if (auth == null)
            {
                return new TaskResult(false, "Could not authorize user.");
            }

            var user = await Context.Users.FindAsync(subscription.UserId);

            if (auth.UserId != user.Id)
            {
                return new TaskResult(false, "Mismatch between auth and requested user notification hook.");
            }

            if (user == null)
            {
                return new TaskResult(false, $"Could not find user with Id {subscription.UserId}");
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
     */
}
