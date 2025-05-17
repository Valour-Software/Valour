namespace Valour.Shared.Models;

public class PlanetMemberQueryModel : QueryModel
{
    public long PlanetId { get; set; }

    public override string GetApiUrl() => $"api/planets/{PlanetId}/members";
}
