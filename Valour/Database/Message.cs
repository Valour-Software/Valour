using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class Message : ISharedMessage
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    public Planet Planet { get; set; }
    public User AuthorUser { get; set; }
    public PlanetMember AuthorMember { get; set; }
    public Message ReplyToMessage { get; set; }
    public Channel Channel { get; set; }
    
    public virtual ICollection<Message> Replies { get; set; }
    public virtual ICollection<MessageReaction> Reactions { get; set; }
    public virtual ICollection<MessageAttachment> Attachments { get; set; }
    public virtual ICollection<MessageMention> Mentions { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    public long Id { get; set; }
    public long? PlanetId { get; set; }

    /// <summary>
    /// The message (if any) this is a reply to
    /// </summary>
    public long? ReplyToId { get; set; }

    /// <summary>
    /// The author's user ID
    /// </summary>
    public long AuthorUserId { get; set; }

    /// <summary>
    /// The author's member ID
    /// </summary>
    public long? AuthorMemberId { get; set; }

    /// <summary>
    /// String representation of message
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// The time the message was sent (in UTC)
    /// </summary>
    public DateTime TimeSent { get; set; }

    /// <summary>
    /// Id of the channel this message belonged to
    /// </summary>
    public long ChannelId { get; set; }

    /// <summary>
    /// The time when the message was edited, or null if it was not
    /// </summary>
    public DateTime? EditedTime { get; set; }

    /// <summary>
    /// Server-managed provenance for content imported from another service or
    /// federation node. Null for content created natively on this instance.
    /// </summary>
    public string ImportSource { get; set; }

    /// <summary>
    /// Non-null when this message was sent through a webhook. No FK: messages
    /// outlive webhook deletion.
    /// </summary>
    public long? WebhookId { get; set; }

    /// <summary>
    /// Display-name override for webhook messages, stamped at send time.
    /// </summary>
    public string OverrideName { get; set; }

    /// <summary>
    /// Immutable Valour CDN avatar asset used by this webhook message.
    /// </summary>
    public long? WebhookAvatarAssetId { get; set; }

    public bool WebhookAvatarAnimated { get; set; }

    /// <summary>
    /// How the text is stored. See <c>MessageEncryption</c>: 0 is plain text,
    /// 1 is end-to-end encrypted, 2 is sealed by the server to the channel key.
    /// When nonzero, <see cref="Content"/> is empty and the text is in
    /// <see cref="Envelope"/>.
    /// </summary>
    public int EncryptionVersion { get; set; }

    /// <summary>
    /// The encrypted message: signed header, ciphertext, and signature.
    /// </summary>
    public byte[] Envelope { get; set; }

    /// <summary>
    /// The channel key generation the envelope was encrypted with.
    /// </summary>
    public int KeyGeneration { get; set; }

    /// <summary>
    /// Keyed search terms computed by a client. The server matches them for
    /// search and automod without learning the words.
    /// </summary>
    public int[] SearchTerms { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<Message>(e =>
        {
            // Table
            e.ToTable("messages");
            
            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");
            
            e.Property(x => x.ReplyToId)
                .HasColumnName("reply_to_id");
            
            e.Property(x => x.AuthorUserId)
                .HasColumnName("author_user_id");
            
            e.Property(x => x.AuthorMemberId)
                .HasColumnName("author_member_id");
            
            e.Property(x => x.Content)
                .HasColumnName("content");
            
            e.Property(x => x.TimeSent)
                .HasColumnName("time_sent")
                .HasConversion(x => x, x => 
                    new DateTime(x.Ticks, DateTimeKind.Utc)
                );
            
            e.Property(x => x.ChannelId)
                .HasColumnName("channel_id");
            
            e.Property(x => x.EditedTime)
                .HasColumnName("edit_time")
                .HasConversion(x => x, x =>
                    x == null ? null : new DateTime(x.Value.Ticks, DateTimeKind.Utc)
                );

            e.Property(x => x.ImportSource)
                .HasColumnName("import_source")
                .HasMaxLength(256);

            e.Property(x => x.WebhookId)
                .HasColumnName("webhook_id");

            e.Property(x => x.OverrideName)
                .HasColumnName("override_name")
                .HasMaxLength(32);

            e.Property(x => x.WebhookAvatarAssetId)
                .HasColumnName("webhook_avatar_asset_id");

            e.Property(x => x.WebhookAvatarAnimated)
                .HasColumnName("webhook_avatar_animated");

            e.Property(x => x.EncryptionVersion)
                .HasColumnName("encryption_version");

            e.Property(x => x.Envelope)
                .HasColumnName("envelope");

            e.Property(x => x.KeyGeneration)
                .HasColumnName("key_generation");

            e.Property(x => x.SearchTerms)
                .HasColumnName("search_terms");
            
            // Keys
            e.HasKey(x => x.Id);
            
            // Relationships
            e.HasOne(x => x.Planet)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.PlanetId);
            
            e.HasOne(x => x.AuthorUser)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.AuthorUserId);
            
            e.HasOne(x => x.AuthorMember)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.AuthorMemberId);
            
            // SetNull so deleting a message never fails because something
            // replied to it; replies simply lose the reference
            e.HasOne(x => x.ReplyToMessage)
                .WithMany(x => x.Replies)
                .HasForeignKey(x => x.ReplyToId)
                .OnDelete(DeleteBehavior.SetNull);
            
            e.HasOne(x => x.Channel)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ChannelId);
            
            e.HasMany(x => x.Reactions)
                .WithOne(x => x.Message)
                .HasForeignKey(x => x.MessageId);

            e.HasMany(x => x.Attachments)
                .WithOne(x => x.Message)
                .HasForeignKey(x => x.MessageId);

            e.HasMany(x => x.Mentions)
                .WithOne(x => x.Message)
                .HasForeignKey(x => x.MessageId);

            // Indices
            e.HasIndex(x => x.PlanetId);
            // Channel history pages filter by channel and walk ids downward, so the
            // composite index serves them directly and also covers channel lookups
            e.HasIndex(x => new { x.ChannelId, x.Id });
            e.HasIndex(x => x.TimeSent);
        });
    }
}

