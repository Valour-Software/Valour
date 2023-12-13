using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("reactions")]
public class Reaction : ISharedReaction
{
    // Foreign keys
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    [ForeignKey("MessageId")]
    public virtual Message Message { get; set; }
    
    /// <summary>
    /// The id of this reaction
    /// </summary>
    [Column("id")]
    public long Id { get; set; }
    
    /// <summary>
    /// The id of the message this reaction is on
    /// </summary>
    [Column("message_id")]
    public long MessageId { get; set; }
    
    /// <summary>
    /// The user who added the reaction
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }
}