using Valour.Shared.Models;

namespace Valour.Server.Models;

public class TenorFavorite : ServerModel<long>, ISharedTenorFavorite
{
    public long UserId { get; set; }
    
    public string TenorId { get; set; }
}