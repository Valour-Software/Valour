using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Database;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// Community events for planets — list, manage, and RSVP
/// </summary>
public class PlanetEventApi
{
    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/events")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAllAsync(
        long planetId,
        PlanetMemberService memberService,
        ValourDb db,
        [FromQuery] DateTime? start = null,
        [FromQuery] DateTime? end = null)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var calendarEnabled = await db.Planets.AsNoTracking()
            .Where(x => x.Id == planetId)
            .Select(x => x.EnableCalendar)
            .FirstOrDefaultAsync();
        if (!calendarEnabled)
            return ValourResult.BadRequest("The calendar is disabled for this planet.");

        var rangeStart = DateTime.SpecifyKind(start ?? DateTime.UtcNow.AddDays(-30), DateTimeKind.Utc);
        var rangeEnd = DateTime.SpecifyKind(end ?? DateTime.UtcNow.AddDays(365), DateTimeKind.Utc);

        var events = await db.PlanetEvents.AsNoTracking()
            .Where(x => x.PlanetId == planetId && !x.IsDeleted &&
                        x.StartsAt >= rangeStart && x.StartsAt <= rangeEnd)
            .OrderBy(x => x.StartsAt)
            .Take(500)
            .ToListAsync();

        var eventIds = events.Select(x => x.Id).ToList();

        var goingCounts = await db.PlanetEventRsvps.AsNoTracking()
            .Where(x => eventIds.Contains(x.EventId))
            .GroupBy(x => x.EventId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);

        var selfRsvps = await db.PlanetEventRsvps.AsNoTracking()
            .Where(x => eventIds.Contains(x.EventId) && x.UserId == member.UserId)
            .Select(x => new { x.EventId, x.ReminderMinutes })
            .ToDictionaryAsync(x => x.EventId, x => x.ReminderMinutes);

        var result = events.Select(x => new PlanetEventData
        {
            Id = x.Id,
            PlanetId = x.PlanetId,
            Title = x.Title,
            Description = x.Description,
            Location = x.Location,
            StartsAt = DateTime.SpecifyKind(x.StartsAt, DateTimeKind.Utc),
            EndsAt = x.EndsAt is null ? null : DateTime.SpecifyKind(x.EndsAt.Value, DateTimeKind.Utc),
            GoingCount = goingCounts.GetValueOrDefault(x.Id),
            SelfGoing = selfRsvps.ContainsKey(x.Id),
            SelfReminderMinutes = selfRsvps.GetValueOrDefault(x.Id),
        }).ToList();

        return Results.Json(result);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/events")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostAsync(
        long planetId,
        [FromBody] PlanetEventData eventData,
        PlanetMemberService memberService,
        ValourDb db)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageCalendar))
            return ValourResult.LacksPermission(PlanetPermissions.ManageCalendar);

        var calendarEnabled = await db.Planets.AsNoTracking()
            .Where(x => x.Id == planetId)
            .Select(x => x.EnableCalendar)
            .FirstOrDefaultAsync();
        if (!calendarEnabled)
            return ValourResult.BadRequest("The calendar is disabled for this planet.");

        var error = Validate(eventData);
        if (error is not null)
            return ValourResult.BadRequest(error);

        var dbEvent = new Valour.Database.PlanetEvent
        {
            Id = IdManager.Generate(),
            PlanetId = planetId,
            AuthorUserId = member.UserId,
            Title = eventData.Title.Trim(),
            Description = eventData.Description?.Trim() ?? string.Empty,
            Location = eventData.Location?.Trim() ?? string.Empty,
            StartsAt = DateTime.SpecifyKind(eventData.StartsAt, DateTimeKind.Utc),
            EndsAt = eventData.EndsAt is null
                ? null
                : DateTime.SpecifyKind(eventData.EndsAt.Value, DateTimeKind.Utc),
            TimeCreated = DateTime.UtcNow,
        };

        db.PlanetEvents.Add(dbEvent);
        await db.SaveChangesAsync();

        eventData.Id = dbEvent.Id;
        eventData.PlanetId = planetId;
        return Results.Json(eventData);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/events/{eventId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutAsync(
        long planetId,
        long eventId,
        [FromBody] PlanetEventData eventData,
        PlanetMemberService memberService,
        ValourDb db)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageCalendar))
            return ValourResult.LacksPermission(PlanetPermissions.ManageCalendar);

        var error = Validate(eventData);
        if (error is not null)
            return ValourResult.BadRequest(error);

        var dbEvent = await db.PlanetEvents
            .FirstOrDefaultAsync(x => x.Id == eventId && x.PlanetId == planetId && !x.IsDeleted);
        if (dbEvent is null)
            return ValourResult.NotFound("Event not found.");

        dbEvent.Title = eventData.Title.Trim();
        dbEvent.Description = eventData.Description?.Trim() ?? string.Empty;
        dbEvent.Location = eventData.Location?.Trim() ?? string.Empty;
        dbEvent.StartsAt = DateTime.SpecifyKind(eventData.StartsAt, DateTimeKind.Utc);
        dbEvent.EndsAt = eventData.EndsAt is null
            ? null
            : DateTime.SpecifyKind(eventData.EndsAt.Value, DateTimeKind.Utc);

        await db.SaveChangesAsync();
        return Results.Json(eventData);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/events/{eventId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteAsync(
        long planetId,
        long eventId,
        PlanetMemberService memberService,
        ValourDb db)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageCalendar))
            return ValourResult.LacksPermission(PlanetPermissions.ManageCalendar);

        var dbEvent = await db.PlanetEvents
            .FirstOrDefaultAsync(x => x.Id == eventId && x.PlanetId == planetId && !x.IsDeleted);
        if (dbEvent is null)
            return ValourResult.NotFound("Event not found.");

        dbEvent.IsDeleted = true;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/events/{eventId}/rsvp")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> RsvpAsync(
        long planetId,
        long eventId,
        PlanetMemberService memberService,
        ValourDb db,
        [FromQuery] int? reminderMinutes = null)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (reminderMinutes is < 0 or > 10080)
            return ValourResult.BadRequest("Reminder must be between 0 minutes and 1 week before the event.");

        var exists = await db.PlanetEvents.AsNoTracking()
            .AnyAsync(x => x.Id == eventId && x.PlanetId == planetId && !x.IsDeleted);
        if (!exists)
            return ValourResult.NotFound("Event not found.");

        var rsvp = await db.PlanetEventRsvps
            .FirstOrDefaultAsync(x => x.EventId == eventId && x.UserId == member.UserId);
        if (rsvp is null)
        {
            db.PlanetEventRsvps.Add(new Valour.Database.PlanetEventRsvp
            {
                EventId = eventId,
                UserId = member.UserId,
                TimeCreated = DateTime.UtcNow,
                ReminderMinutes = reminderMinutes,
            });
        }
        else if (rsvp.ReminderMinutes != reminderMinutes)
        {
            // Changing the lead time re-arms the reminder
            rsvp.ReminderMinutes = reminderMinutes;
            rsvp.ReminderSentAt = null;
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/events/{eventId}/rsvp")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> UnRsvpAsync(
        long planetId,
        long eventId,
        PlanetMemberService memberService,
        ValourDb db)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var rsvp = await db.PlanetEventRsvps
            .FirstOrDefaultAsync(x => x.EventId == eventId && x.UserId == member.UserId);
        if (rsvp is not null)
        {
            db.PlanetEventRsvps.Remove(rsvp);
            await db.SaveChangesAsync();
        }

        return Results.NoContent();
    }

    private static string Validate(PlanetEventData eventData)
    {
        if (eventData is null)
            return "Include event in body.";
        if (string.IsNullOrWhiteSpace(eventData.Title))
            return "Title is required.";
        if (eventData.Title.Length > PlanetEventData.MaxTitleLength)
            return $"Title must be at most {PlanetEventData.MaxTitleLength} characters.";
        if (eventData.Description?.Length > PlanetEventData.MaxDescriptionLength)
            return $"Description must be at most {PlanetEventData.MaxDescriptionLength} characters.";
        if (eventData.Location?.Length > PlanetEventData.MaxLocationLength)
            return $"Location must be at most {PlanetEventData.MaxLocationLength} characters.";
        if (eventData.StartsAt == default)
            return "Start time is required.";
        if (eventData.EndsAt is not null && eventData.EndsAt <= eventData.StartsAt)
            return "End time must be after the start time.";
        return null;
    }
}
