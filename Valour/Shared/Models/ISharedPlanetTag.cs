namespace Valour.Shared.Models;

public interface ISharedPlanetTag : ISharedModel<long>
{
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public string Slug { get; set; }

    /// <summary>
    /// True only for the official seed tags. Curated tags are the only ones
    /// surfaced in onboarding; user-created tags never get this flag.
    /// </summary>
    public bool Curated { get; set; }
}