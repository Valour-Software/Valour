#nullable enable

namespace Valour.Shared.Models.Wiki;

/// <summary>
/// The markdown content of a doc page, fetched separately from the metadata
/// model so tree loads and realtime broadcasts stay lightweight.
/// </summary>
public class WikiPageContent
{
    public long PageId { get; set; }
    public long PlanetId { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The doc version this content corresponds to
    /// </summary>
    public long Version { get; set; }
}

public class WikiPageCreateRequest
{
    public long? ParentId { get; set; }
    public bool IsFolder { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit slug; when null or empty the server derives one from
    /// the title. Ignored for folders.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Initial markdown content. Ignored for folders.
    /// </summary>
    public string? Content { get; set; }

    public bool IsPublished { get; set; } = true;
}

public class WikiPageContentUpdateRequest
{
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// The doc version the editor started from. A mismatch with the current
    /// version means someone else saved in between; the server rejects with
    /// 409 so the client can resolve.
    /// </summary>
    public long BaseVersion { get; set; }
}

public class WikiPageMoveRequest
{
    /// <summary>
    /// The destination folder; null moves the node to the root
    /// </summary>
    public long? NewParentId { get; set; }

    /// <summary>
    /// Local position among the destination siblings (0-based; clamped)
    /// </summary>
    public uint NewPosition { get; set; }
}

public class WikiSearchResult
{
    public long PageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}
