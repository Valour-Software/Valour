using Valour.Shared.Models.Wiki;

namespace Valour.Server.Models;

/// <summary>
/// Wire/metadata model for a docs tree node. Deliberately has no Content
/// property — page markdown is served through dedicated content endpoints so
/// tree fetches and realtime broadcasts stay small.
/// </summary>
public class PlanetWikiPage : ServerModel<long>, ISharedPlanetWikiPage
{
    public long PlanetId { get; set; }
    public long? ParentId { get; set; }
    public bool IsFolder { get; set; }
    public string Slug { get; set; }
    public string PreviousSlug { get; set; }
    public string Title { get; set; }
    public uint Position { get; set; }
    public bool IsPublished { get; set; }
    public long Version { get; set; }
    public DateTime TimeCreated { get; set; }
    public DateTime? LastEdited { get; set; }
    public long CreatedByUserId { get; set; }
    public long? LastEditedByUserId { get; set; }
}
