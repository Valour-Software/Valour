#nullable enable

using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

public class MessageReaction
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    /// <summary>
    /// The message this reaction belongs to
    /// </summary>
    public virtual required Message Message { get; set; }
    
    /// <summary>
    /// The user who reacted to this message
    /// </summary>
    public virtual required User AuthorUser { get; set; }
    
    /// <summary>
    /// If in a planet channel, the member who reacted to this message
    /// </summary>
    public virtual PlanetMember? AuthorMember { get; set; }
    
    /// <summary>
    /// The ID of the reaction
    /// </summary>
    public long Id { get; set; }
    
    /// <summary>
    /// The emoji used for the reaction
    /// </summary>
    public required string Emoji { get; set; }
    
    /// <summary>
    /// The message this reaction belongs to
    /// </summary>
    public long MessageId { get; set; }
    
    /// <summary>
    /// The ID of the user who reacted
    /// </summary>
    public long AuthorUserId { get; set; }
    
    /// <summary>
    /// If in a planet channel, the ID of the member this reaction belongs to
    /// </summary>
    public long? AuthorMemberId { get; set; }
    
    /// <summary>
    /// The time this reaction was created
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<MessageReaction>(e =>
        {
            // Table
            e.ToTable("message_reactions");
            
            // Keys
            e.HasKey(x => x.Id);
            
            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.Emoji)
                .HasColumnName("emoji")
                .IsRequired();
            
            e.Property(x => x.MessageId)
                .HasColumnName("message_id")
                .IsRequired();
            
            e.Property(x => x.AuthorUserId)
                .HasColumnName("author_user_id")
                .IsRequired();
            
            e.Property(x => x.AuthorMemberId)
                .HasColumnName("author_member_id");
            
            e.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();
            
            // Relationships
            e.HasOne(x => x.Message)
                .WithMany(x => x.Reactions)
                .HasForeignKey(x => x.MessageId);
            
            e.HasOne(x => x.AuthorUser)
                .WithMany(x => x.MessageReactions)
                .HasForeignKey(x => x.AuthorUserId);
            
            e.HasOne(x => x.AuthorMember)
                .WithMany()
                .HasForeignKey(x => x.AuthorMemberId);

            // VERY important index
            e.HasIndex(x => x.MessageId);
        });
    }
}