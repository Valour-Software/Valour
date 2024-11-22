using Valour.Shared.Models;

namespace Valour.Server.Models;

public class TenorFavorite : ServerModel, ISharedTenorFavorite
{
    public new long Id { get; set; }
    
    public long UserId { get; set; }
    
    public string TenorId { get; set; }
}