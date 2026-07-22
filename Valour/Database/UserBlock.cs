using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("user_blocks")]
[Index(nameof(UserId), nameof(BlockedUserId), IsUnique = true)]
[Index(nameof(BlockedUserId))]
public class UserBlock : ISharedUserBlock
{
    [ForeignKey("UserId")]
    public virtual User User { get; set; }

    [ForeignKey("BlockedUserId")]
    public virtual User BlockedUser { get; set; }

    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("blocked_user_id")]
    public long BlockedUserId { get; set; }

    [Column("block_type")]
    public BlockType BlockType { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
