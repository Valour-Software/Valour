using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models.Themes;

namespace Valour.Database.Themes;

[Table("theme_votes")]
public class ThemeVote : ISharedThemeVote
{
    [ForeignKey("ThemeId")]
    public virtual Theme Theme { get; set; }
    
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    [Column("id")]
    public long Id { get; set; }
    
    [Column("theme_id")]
    public long ThemeId { get; set; }
    
    [Column("user_id")]
    public long UserId { get; set; }
    
    [Column("sentiment")]
    public bool Sentiment { get; set; }
    
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}