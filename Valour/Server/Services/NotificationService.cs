using System.Text.Json;
using Valour.Server.Config;
using Valour.Server.Database;
using Valour.Shared.Models;
using WebPush;

namespace Valour.Server.Services;

public class NotificationService
{
    private static VapidDetails _vapidDetails;
    private static WebPushClient _webPush;
    
    private readonly ValourDB _db;
    private readonly UserService _userService;
    private readonly CoreHubService _coreHub;
    private readonly NodeService _nodeService;
    
    public NotificationService(
        ValourDB db, 
        UserService userService, 
        CoreHubService coreHub,
        NodeService nodeService)
    {
        _db = db;
        _userService = userService;
        _coreHub = coreHub;
        _nodeService = nodeService;
        
        if (_vapidDetails is null)
            _vapidDetails = new VapidDetails(VapidConfig.Current.Subject, VapidConfig.Current.PublicKey, VapidConfig.Current.PrivateKey);
        
        if (_webPush is null)
            _webPush = new WebPushClient();
    }

    public async Task<List<Notification>> GetNotifications(long userId, int page = 0)
        => await _db.Notifications.Where(x => x.UserId == userId)
            .OrderByDescending(x => x.TimeSent)
            .Skip(page * 50)
            .Take(50)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    public async Task<List<Notification>> GetUnreadNotifications(long userId, int page = 0)
        => await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null)
            .OrderByDescending(x => x.TimeSent)
            .Skip(page * 50)
            .Take(50)
            .Select(x => x.ToModel())
            .ToListAsync();

    public async Task AddNotificationAsync(Notification notification)
    {
        // Send actual push notification to devices
        await SendPushNotificationAsync(
            notification.UserId, 
            notification.ImageUrl, 
            notification.Title, 
            notification.Body, 
            notification.ClickUrl
        );

        // Create id for notification
        notification.Id = IdManager.Generate();
        // Set time of notification
        notification.TimeSent = DateTime.UtcNow;
        
        // Add notification to database
        await _db.Notifications.AddAsync(notification.ToDatabase());
        await _db.SaveChangesAsync();
        
        // Send notification to all of the user's online Valour instances
        _coreHub.RelayNotification(notification, _nodeService);
    }

    public async Task SendPushNotificationAsync(long userId, string iconUrl, string title, string message, string clickUrl)
    {
        // Get all subscriptions for user
        var subs = await _db.NotificationSubscriptions.Where(x => x.UserId == userId).ToListAsync();

        // Send notification to all
        foreach (var sub in subs)
        {
            var pushSubscription = new PushSubscription(sub.Endpoint, sub.Key, sub.Auth);
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    title,
                    message,
                    iconUrl,
                    url = $"",
                });

                await _webPush.SendNotificationAsync(pushSubscription, payload, _vapidDetails);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error sending push notification: " + ex.Message);
            }
        }
    }
}