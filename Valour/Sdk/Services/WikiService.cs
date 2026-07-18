using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models;
using Valour.Sdk.Models.Wiki;
using Valour.Shared;
using Valour.Shared.Models.Wiki;

namespace Valour.Sdk.Services;

/// <summary>
/// Provides access to planet docs/wiki trees, page content, revisions, search,
/// and the public docs vanity name.
/// </summary>
public class WikiService : ServiceBase
{
    public const int ConflictErrorCode = 409;

    private static readonly LogOptions LogOptions = new(
        "WikiService",
        "#3381a3",
        "#a33340",
        "#a39433"
    );

    private readonly ValourClient _client;

    /// <summary>
    /// Page content cached per doc id. Entries are validated against the
    /// synced metadata model's Version on read, so realtime metadata updates
    /// invalidate stale content automatically.
    /// </summary>
    private readonly Dictionary<long, WikiPageContent> _contentCache = new();

    public WikiService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }

    /////////////
    // Fetches //
    /////////////

    public async ValueTask<PlanetWikiPage> FetchWikiPageAsync(long planetId, long pageId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId);
        if (planet is null)
            return null;

        if (!skipCache && planet.WikiPages.TryGet(pageId, out var cached))
            return cached;

        var response = await planet.Node.GetJsonAsync<PlanetWikiPage>(
            ISharedPlanetWikiPage.GetIdRoute(planetId, pageId), true);

        return response.Data?.Sync(_client);
    }

    public async ValueTask<PlanetWikiPage> FetchWikiPageBySlugAsync(long planetId, string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var planet = await _client.PlanetService.FetchPlanetAsync(planetId);
        if (planet is null)
            return null;

        slug = slug.Trim().ToLowerInvariant();

        var cached = planet.WikiPages.FirstOrDefault(x => x.Slug == slug);
        if (cached is not null)
            return cached;

        var response = await planet.Node.GetJsonAsync<PlanetWikiPage>(
            ISharedPlanetWikiPage.GetBySlugRoute(planetId, slug), true);

        return response.Data?.Sync(_client);
    }

    /// <summary>
    /// Fetches a page's markdown, using the version-validated content cache
    /// </summary>
    public async Task<TaskResult<WikiPageContent>> FetchContentAsync(PlanetWikiPage doc, bool skipCache = false)
    {
        if (doc is null)
            return TaskResult<WikiPageContent>.FromFailure("Doc is required.");

        if (!skipCache &&
            _contentCache.TryGetValue(doc.Id, out var cached) &&
            cached.Version == doc.Version)
        {
            return TaskResult<WikiPageContent>.FromData(cached);
        }

        var response = await doc.Node.GetJsonAsync<WikiPageContent>(
            ISharedPlanetWikiPage.GetContentRoute(doc.PlanetId, doc.Id));

        if (response.Success && response.Data is not null)
            _contentCache[doc.Id] = response.Data;

        return response;
    }

    ///////////////
    // Mutations //
    ///////////////

    public async Task<TaskResult<PlanetWikiPage>> CreateWikiPageAsync(Planet planet, WikiPageCreateRequest request)
    {
        var response = await planet.Node.PostAsyncWithResponse<PlanetWikiPage>(
            ISharedPlanetWikiPage.GetBaseRoute(planet.Id), request);

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    /// <summary>
    /// Saves metadata changes (title, slug, publish state)
    /// </summary>
    public async Task<TaskResult<PlanetWikiPage>> UpdateWikiPageAsync(PlanetWikiPage doc)
    {
        var response = await doc.Node.PutAsyncWithResponse<PlanetWikiPage>(doc.IdRoute, doc);

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    /// <summary>
    /// Saves page content. A failure with Code 409 means the page changed
    /// since baseVersion was read — the caller should offer reload/overwrite.
    /// </summary>
    public async Task<TaskResult<PlanetWikiPage>> SaveContentAsync(PlanetWikiPage doc, string content, long baseVersion)
    {
        var request = new WikiPageContentUpdateRequest
        {
            Content = content,
            BaseVersion = baseVersion,
        };

        var response = await doc.Node.PutAsyncWithResponse<PlanetWikiPage>(
            ISharedPlanetWikiPage.GetContentRoute(doc.PlanetId, doc.Id), request);

        if (response.Success && response.Data is not null)
        {
            response.Data.Sync(_client);
            _contentCache[doc.Id] = new WikiPageContent
            {
                PageId = doc.Id,
                PlanetId = doc.PlanetId,
                Content = content,
                Version = response.Data.Version,
            };
        }

        return response;
    }

    public async Task<TaskResult> MoveWikiPageAsync(PlanetWikiPage doc, long? newParentId, uint newPosition)
    {
        var request = new WikiPageMoveRequest
        {
            NewParentId = newParentId,
            NewPosition = newPosition,
        };

        return await doc.Node.PostAsync(
            ISharedPlanetWikiPage.GetMoveRoute(doc.PlanetId, doc.Id), request);
    }

    public async Task<TaskResult> DeleteWikiPageAsync(PlanetWikiPage doc)
    {
        var response = await doc.Node.DeleteAsync(doc.IdRoute);

        if (response.Success)
        {
            doc.RemoveFromCache();
            _contentCache.Remove(doc.Id);
        }

        return response;
    }

    ///////////////
    // Revisions //
    ///////////////

    public async Task<TaskResult<List<PlanetWikiRevision>>> FetchRevisionsAsync(PlanetWikiPage doc) =>
        await doc.Node.GetJsonAsync<List<PlanetWikiRevision>>(
            ISharedPlanetWikiPage.GetRevisionsRoute(doc.PlanetId, doc.Id));

    public async Task<TaskResult<PlanetWikiRevision>> FetchRevisionAsync(PlanetWikiPage doc, long revisionId) =>
        await doc.Node.GetJsonAsync<PlanetWikiRevision>(
            ISharedPlanetWikiPage.GetRevisionRoute(doc.PlanetId, doc.Id, revisionId));

    public async Task<TaskResult<PlanetWikiPage>> RestoreRevisionAsync(PlanetWikiPage doc, long revisionId)
    {
        var response = await doc.Node.PostAsyncWithResponse<PlanetWikiPage>(
            ISharedPlanetWikiPage.GetRestoreRoute(doc.PlanetId, doc.Id, revisionId));

        if (response.Success && response.Data is not null)
        {
            response.Data.Sync(_client);
            // Restored content differs from whatever is cached
            _contentCache.Remove(doc.Id);
        }

        return response;
    }

    ////////////
    // Search //
    ////////////

    public async Task<TaskResult<List<WikiSearchResult>>> SearchAsync(Planet planet, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return TaskResult<List<WikiSearchResult>>.FromData(new List<WikiSearchResult>());

        var route = $"{ISharedPlanetWikiPage.GetSearchRoute(planet.Id)}?q={Uri.EscapeDataString(query.Trim())}";
        return await planet.Node.GetJsonAsync<List<WikiSearchResult>>(route);
    }

}
