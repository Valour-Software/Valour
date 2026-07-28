using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Server.Workers;

/// <summary>
/// Sends event reminder notifications for calendar RSVPs that opted into a
/// reminder. Each reminder fires once: reminder_sent_at marks delivery, and
/// changing the lead time re-arms it.
/// </summary>
public class CalendarReminderWorker : BackgroundService
{
    private readonly ILogger<CalendarReminderWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // Reminders max out at one week before start; scan a little past that
    private static readonly TimeSpan ScanWindow = TimeSpan.FromDays(8);

    public CalendarReminderWorker(
        ILogger<CalendarReminderWorker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendDueRemindersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending calendar event reminders");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task SendDueRemindersAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now = DateTime.UtcNow;
        var windowEnd = now + ScanWindow;

        var candidates = await db.PlanetEventRsvps
            .Where(x => x.ReminderMinutes != null
                        && x.ReminderSentAt == null
                        && !x.Event.IsDeleted
                        && x.Event.StartsAt > now
                        && x.Event.StartsAt <= windowEnd)
            .Select(x => new
            {
                Rsvp = x,
                x.Event.PlanetId,
                x.Event.StartsAt,
                x.Event.Title,
            })
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            var startsAt = DateTime.SpecifyKind(candidate.StartsAt, DateTimeKind.Utc);
            if (startsAt.AddMinutes(-candidate.Rsvp.ReminderMinutes!.Value) > now)
                continue;

            var minutesLeft = (int)Math.Max(0, Math.Round((startsAt - now).TotalMinutes));
            var body = minutesLeft switch
            {
                0 => "Starting now",
                < 60 => $"Starts in {minutesLeft} minute{(minutesLeft == 1 ? "" : "s")}",
                < 1440 => $"Starts in {(int)Math.Round(minutesLeft / 60.0)} hour{(Math.Round(minutesLeft / 60.0) == 1 ? "" : "s")}",
                _ => $"Starts in {(int)Math.Round(minutesLeft / 1440.0)} day{(Math.Round(minutesLeft / 1440.0) == 1 ? "" : "s")}",
            };

            candidate.Rsvp.ReminderSentAt = now;
            await db.SaveChangesAsync();

            await notificationService.SendUserNotification(candidate.Rsvp.UserId, new Models.Notification
            {
                Title = $"📅 {candidate.Title}",
                Body = body,
                Source = NotificationSource.EventReminder,
                SourceId = candidate.Rsvp.EventId,
                PlanetId = candidate.PlanetId,
            });
        }
    }
}
