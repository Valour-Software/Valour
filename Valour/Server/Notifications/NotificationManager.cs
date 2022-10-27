using System.Text.Json;
using Valour.Server.Config;
using Valour.Server.Database;
using WebPush;

namespace Valour.Server.Notifications
{
    public static class NotificationManager
    {
        public static async Task SendNotificationAsync(ValourDB db, long userId, string iconUrl, string title, string message)
        {
            var publicKey = VapidConfig.Current.PublicKey;
            var privateKey = VapidConfig.Current.PrivateKey;
            var mailTo = VapidConfig.Current.Subject;

            // Get all subscriptions for user
            var subs = await db.NotificationSubscriptions.Where(x => x.UserId == userId).ToListAsync();

            // Send notification to all
            foreach (var sub in subs)
            {
                var pushSubscription = new PushSubscription(sub.Endpoint, sub.Key, sub.Auth);
                var vapidDetails = new VapidDetails(mailTo, publicKey, privateKey);
                var webPushClient = new WebPushClient();
                try
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        title,
                        message,
                        iconUrl,
                        url = $"",
                    });

                    await webPushClient.SendNotificationAsync(pushSubscription, payload, vapidDetails);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error sending push notification: " + ex.Message);
                }
            }
        }
    }
}
