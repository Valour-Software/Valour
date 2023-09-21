using System.Text.Json;
using Markdig.Extensions.TaskLists;
using Valour.Server.Config;
using Valour.Server.Database;
using Valour.Shared;
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
    private readonly IServiceScopeFactory _scopeFactory;
    
    public NotificationService(
        ValourDB db, 
        UserService userService, 
        CoreHubService coreHub,
        NodeService nodeService,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _userService = userService;
        _coreHub = coreHub;
        _nodeService = nodeService;
        _scopeFactory = scopeFactory;
        
        if (_vapidDetails is null)
            _vapidDetails = new VapidDetails(VapidConfig.Current.Subject, VapidConfig.Current.PublicKey, VapidConfig.Current.PrivateKey);
        
        if (_webPush is null)
            _webPush = new WebPushClient();
    }
    
    public async Task<Notification> GetNotificationAsync(long id)
        => (await _db.Notifications.FindAsync(id)).ToModel();

    public async Task<TaskResult> SetNotificationRead(long id, bool value)
    {
        var notification = await _db.Notifications.FindAsync(id);
        if (notification is null)
            return new TaskResult(false, "Notification not found");

        if (value)
        {
            notification.TimeRead = DateTime.UtcNow;
        }
        else
        {
            notification.TimeRead = null;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception)
        {
            return new TaskResult(false, "Error saving changes to database");
        }

        _coreHub.RelayNotificationReadChange(notification.ToModel(), _nodeService);
        
        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> ClearNotificationsForUser(long userId)
    {
        int changes = 0;
        
        try
        {
            changes = await _db.Database.ExecuteSqlRawAsync("UPDATE notifications SET time_read = (now() at time zone 'utc') WHERE user_id = {0} AND time_read IS NULL;",
                userId);
        }
        catch (Exception)
        {
            return new TaskResult(false, "Error saving changes to database");
        }
        
        _coreHub.RelayNotificationsCleared(userId, _nodeService);

        return new TaskResult(true, $"Cleared {changes} notifications");
    }

    public async Task<List<Notification>> GetNotifications(long userId, int page = 0)
        => await _db.Notifications.Where(x => x.UserId == userId)
            .OrderBy(x => x.TimeSent)
            .Skip(page * 50)
            .Take(50)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    public async Task<List<Notification>> GetUnreadNotifications(long userId, int page = 0)
        => await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null)
            .OrderBy(x => x.TimeSent)
            .Skip(page * 50)
            .Take(50)
            .Select(x => x.ToModel())
            .ToListAsync();
    
    public async Task<List<Notification>> GetAllUnreadNotifications(long userId)
        => await _db.Notifications.Where(x => x.UserId == userId && x.TimeRead == null)
            .OrderBy(x => x.TimeSent)
            .Select(x => x.ToModel())
            .ToListAsync();

    public async Task AddNotificationAsync(Notification notification)
    {
        // Create id for notification
        notification.Id = IdManager.Generate();
        // Set time of notification
        notification.TimeSent = DateTime.UtcNow;
        
        // Add notification to database
        await _db.Notifications.AddAsync(notification.ToDatabase());
        await _db.SaveChangesAsync();

        // Send notification to all of the user's online Valour instances
        _coreHub.RelayNotification(notification, _nodeService);
        
        // Send actual push notification to devices
        await SendPushNotificationAsync(
            notification.UserId, 
            notification.ImageUrl, 
            notification.Title, 
            notification.Body, 
            notification.ClickUrl
        );
    }
    
    public async Task AddBatchedNotificationAsync(Notification notification, ValourDB db)
    {
        // Create id for notification
        notification.Id = IdManager.Generate();
        // Set time of notification
        notification.TimeSent = DateTime.UtcNow;

        // Send notification to all of the user's online Valour instances
        _coreHub.RelayNotification(notification, _nodeService);
        
        // Send actual push notification to devices
        await SendPushNotificationAsync(
            notification.UserId, 
            notification.ImageUrl, 
            notification.Title, 
            notification.Body, 
            notification.ClickUrl,
            db
        );
    }
    
    public Task AddRoleNotificationAsync(Notification baseNotification, long roleId)
    {
        // Do not block entire thread on this
        Task.Run(async () =>
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await using var db = scope.ServiceProvider.GetService<ValourDB>();
        
            var notifications = await db.PlanetRoleMembers.Where(x => x.RoleId == roleId).Select(x => new Notification()
            {
                Id = IdManager.Generate(),
                Title = baseNotification.Title,
                Body = baseNotification.Body,
                ImageUrl = baseNotification.ImageUrl,
                ClickUrl = baseNotification.ClickUrl,
                PlanetId = baseNotification.PlanetId,
                ChannelId = baseNotification.ChannelId,
                Source = NotificationSource.PlanetRoleMention,
                SourceId = baseNotification.SourceId,
                UserId = x.UserId,
                TimeSent = DateTime.UtcNow,
            }).ToListAsync();

            foreach (var notification in notifications)
            {
                await AddBatchedNotificationAsync(notification, db);
            }

            await db.Notifications.AddRangeAsync(notifications.Select(x => x.ToDatabase()));
            await db.SaveChangesAsync();
        });

        return Task.CompletedTask;
    }

    public async Task SendPushNotificationAsync(long userId, string iconUrl, string title, string message, string clickUrl, ValourDB db = null)
    {
        var adb = db ?? _db;
        
        // Get all subscriptions for user
        var subs = await adb.NotificationSubscriptions.Where(x => x.UserId == userId).ToListAsync();
        
        bool dbChange = false;
        
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
                    url = clickUrl,
                });

                // We are not awaiting this on purpose
                #pragma warning disable 4014
                _webPush.SendNotificationAsync(pushSubscription, payload, _vapidDetails);
                #pragma warning restore 4014
            }
            catch (WebPushException wex)
            {
                if (wex.Message.Contains("no longer valid"))
                {
                    // Clean old subscriptions that are now invalid
                    _db.NotificationSubscriptions.Remove(sub);
                    dbChange = true;
                }
                else
                {
                    Console.Error.WriteLine("Error sending push notification: " + wex.Message);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error sending push notification: " + ex.Message);
            }
        }

        if (dbChange)
            await _db.SaveChangesAsync();
    }
}