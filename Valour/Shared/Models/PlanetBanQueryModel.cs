namespace Valour.Shared.Models;

public class PlanetBanQueryModel : QueryModel
{
    public long PlanetId { get; set; }
    public QueryFilter Filter { get; set; }
    public QuerySort Sort { get; set; }

    public override string GetApiUrl() => $"api/planets/{PlanetId}/bans";
}
