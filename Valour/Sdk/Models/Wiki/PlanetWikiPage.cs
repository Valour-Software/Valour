using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models.Wiki;

namespace Valour.Sdk.Models.Wiki;

/// <summary>
/// A node in a planet's docs/wiki tree. Metadata only — page markdown is
/// fetched separately through WikiService so the synced model stays small.
/// </summary>
public class PlanetWikiPage : ClientPlanetModel<PlanetWikiPage, long>, ISharedPlanetWikiPage
{
    public override string BaseRoute => ISharedPlanetWikiPage.GetBaseRoute(PlanetId);

    public override string IdRoute => ISharedPlanetWikiPage.GetIdRoute(PlanetId, Id);

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

    [JsonIgnore]
    public bool IsRoot => ParentId is null;

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private PlanetWikiPage() : base()
    {
    }

    public PlanetWikiPage(ValourClient client) : base(client)
    {
    }

    public override PlanetWikiPage AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Planet.WikiPages.Put(this, flags);
    }

    public override PlanetWikiPage RemoveFromCache(bool skipEvents = false)
    {
        return Planet.WikiPages.Remove(this, skipEvents);
    }
}
