namespace Valour.Shared.Models;

public class InviteScreenModel
{
    public long PlanetId { get; set; }
    public string PlanetName { get; set; }
    public string PlanetImageUrl { get; set; }
    public bool Expired { get; set; }
}