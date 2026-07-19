#nullable enable

namespace Valour.Shared.Models.Wiki;

/// <summary>
/// A node in a planet's docs/wiki tree. A single type covers both folders and
/// pages: folders (<see cref="IsFolder"/>) structure the sidebar and hold no
/// content or slug; pages hold markdown content addressed by a per-planet
/// unique slug. Page content is deliberately NOT part of this model — it
/// travels through dedicated content endpoints so tree fetches and realtime
/// broadcasts stay small.
/// </summary>
public interface ISharedPlanetWikiPage : ISharedPlanetModel<long>, ISortable
{
    public static string GetBaseRoute(long planetId) => $"api/planets/{planetId}/wiki";
    public static string GetTreeRoute(long planetId) => $"{GetBaseRoute(planetId)}/tree";
    public static string GetIdRoute(long planetId, long id) => $"{GetBaseRoute(planetId)}/{id}";
    public static string GetBySlugRoute(long planetId, string slug) => $"{GetBaseRoute(planetId)}/by-slug/{slug}";
    public static string GetContentRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/content";
    public static string GetMoveRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/move";
    public static string GetRevisionsRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/revisions";
    public static string GetRevisionRoute(long planetId, long id, long revisionId) => $"{GetRevisionsRoute(planetId, id)}/{revisionId}";
    public static string GetRestoreRoute(long planetId, long id, long revisionId) => $"{GetRevisionRoute(planetId, id, revisionId)}/restore";
    public static string GetSearchRoute(long planetId) => $"{GetBaseRoute(planetId)}/search";


    public const int MaxTitleLength = 128;
    public const int MaxSlugLength = 64;
    public const int MaxContentLength = 100_000;
    public const int MaxDepth = 6;
    public const int MaxPagesPerPlanet = 1000;
    public const int MaxRevisionsPerPage = 25;

    /// <summary>
    /// The folder this node lives in; null for root-level nodes
    /// </summary>
    long? ParentId { get; set; }

    /// <summary>
    /// Folders organize the tree and cannot hold content
    /// </summary>
    bool IsFolder { get; set; }

    /// <summary>
    /// Per-planet unique URL slug. Pages only; null for folders.
    /// </summary>
    string? Slug { get; set; }

    /// <summary>
    /// The slug this page had before its last rename, used for public 301
    /// redirects. One hop only.
    /// </summary>
    string? PreviousSlug { get; set; }

    string Title { get; set; }

    /// <summary>
    /// Sibling order under <see cref="ParentId"/>. Lower renders first.
    /// </summary>
    uint Position { get; set; }

    /// <summary>
    /// Unpublished pages are hidden from the public site and search, but
    /// remain visible in-app
    /// </summary>
    bool IsPublished { get; set; }

    /// <summary>
    /// Incremented on every content save. Used for client content-cache
    /// invalidation and edit conflict detection.
    /// </summary>
    long Version { get; set; }

    DateTime TimeCreated { get; set; }
    DateTime? LastEdited { get; set; }
    long CreatedByUserId { get; set; }
    long? LastEditedByUserId { get; set; }

    /// <summary>
    /// Non-null when this page originated in an external import.
    /// </summary>
    string? ImportSource { get; set; }

    uint ISortable.GetSortPosition()
    {
        return Position;
    }
}
