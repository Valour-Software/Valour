#nullable enable

namespace Valour.Shared.Models.Wiki;

/// <summary>
/// Wire model for an append-only doc revision snapshot. Revisions are fetched
/// on demand and never cached as synced models, so a single concrete class is
/// shared by the server and SDK. List endpoints omit <see cref="Content"/>.
/// </summary>
public class PlanetWikiRevision
{
    public long Id { get; set; }
    public long PageId { get; set; }
    public long PlanetId { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The full markdown snapshot. Null in list responses; populated when a
    /// single revision is fetched.
    /// </summary>
    public string? Content { get; set; }

    public long AuthorUserId { get; set; }
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// Non-null when this revision originated in an external import.
    /// </summary>
    public string? ImportSource { get; set; }
}
