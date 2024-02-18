using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models.Themes;

namespace Valour.Database.Themes;

[Table("theme_votes")]
public class ThemeVote : ISharedThemeVote
{
    [ForeignKey("UserId")]
    public virtual Theme Theme { get; set; }
    
    [ForeignKey("ThemeId")]
    public virtual User User { get; set; }
    
    [Column("id")]
    public long Id { get; set; }
    
    [Column("theme_id")]
    public long ThemeId { get; set; }
    
    [Column("user_id")]
    public long UserId { get; set; }
}