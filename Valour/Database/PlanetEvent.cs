using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public class PlanetEvent
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public Planet Planet { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public long Id { get; set; }
    public long PlanetId { get; set; }
    public long AuthorUserId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Location { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime TimeCreated { get; set; }
    public bool IsDeleted { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetEvent>(e =>
        {
            e.ToTable("planet_events");

            e.HasKey(x => x.Id);

            e.Property(x => x.Id)
                .HasColumnName("id");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.AuthorUserId)
                .HasColumnName("author_user_id");

            e.Property(x => x.Title)
                .HasColumnName("title")
                .HasMaxLength(128);

            e.Property(x => x.Description)
                .HasColumnName("description")
                .HasMaxLength(2048);

            e.Property(x => x.Location)
                .HasColumnName("location")
                .HasMaxLength(256);

            e.Property(x => x.StartsAt)
                .HasColumnName("starts_at");

            e.Property(x => x.EndsAt)
                .HasColumnName("ends_at");

            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created");

            e.Property(x => x.IsDeleted)
                .HasColumnName("is_deleted");

            e.HasOne(x => x.Planet)
                .WithMany()
                .HasForeignKey(x => x.PlanetId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.PlanetId, x.StartsAt });
        });

        builder.Entity<PlanetEventRsvp>(e =>
        {
            e.ToTable("planet_event_rsvps");

            e.HasKey(x => new { x.EventId, x.UserId });

            e.Property(x => x.EventId)
                .HasColumnName("event_id");

            e.Property(x => x.UserId)
                .HasColumnName("user_id");

            e.Property(x => x.TimeCreated)
                .HasColumnName("time_created");

            e.Property(x => x.ReminderMinutes)
                .HasColumnName("reminder_minutes");

            e.Property(x => x.ReminderSentAt)
                .HasColumnName("reminder_sent_at");

            e.HasOne(x => x.Event)
                .WithMany()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // The reminder worker scans for due, unsent reminders
            e.HasIndex(x => x.ReminderSentAt)
                .HasFilter("reminder_minutes IS NOT NULL AND reminder_sent_at IS NULL");
        });
    }
}

public class PlanetEventRsvp
{
    public PlanetEvent Event { get; set; }

    public long EventId { get; set; }
    public long UserId { get; set; }
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// Minutes before the event start to notify this user; null = no reminder
    /// </summary>
    public int? ReminderMinutes { get; set; }

    /// <summary>
    /// Set once the reminder notification has been sent, so it fires only once
    /// </summary>
    public DateTime? ReminderSentAt { get; set; }
}
