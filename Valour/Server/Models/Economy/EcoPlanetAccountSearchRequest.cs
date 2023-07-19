namespace Valour.Server.Models.Economy;

public class EcoPlanetAccountSearchRequest
{
    public long PlanetId { get; set; }
    public long AccountId { get; set; }
    public string Filter { get; set; }
}