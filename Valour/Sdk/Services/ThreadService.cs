using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Threads;
using Valour.Shared;
using Valour.Shared.Models.Threads;

namespace Valour.Sdk.Services;

/// <summary>
/// Provides access to planet thread feeds, comments, and boosts.
/// </summary>
public class ThreadService : ServiceBase
{
    private static readonly LogOptions LogOptions = new(
        "ThreadService",
        "#3381a3",
        "#a33340",
        "#a39433"
    );

    private readonly ValourClient _client;

    // Boost state for the current user, populated by lookups and toggles
    private readonly HashSet<long> _boostedThreads = new();
    private readonly HashSet<long> _boostedComments = new();

    public ThreadService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }

    /////////////
    // Engines //
    /////////////

    /// <summary>
    /// Returns a query engine for the aggregated feed across all joined planets.
    /// Filters: sort (hot/new/top), period (day/week/all)
    /// </summary>
    public ModelQueryEngine<PlanetThread> GetGlobalFeedEngine() =>
        new(_client.PrimaryNode, ISharedPlanetThread.FeedRoute);

    /// <summary>
    /// Returns a query engine for a specific planet's thread feed.
    /// Filters: sort (hot/new/top), period (day/week/all)
    /// </summary>
    public ModelQueryEngine<PlanetThread> GetPlanetFeedEngine(Planet planet) =>
        new(planet.Node, ISharedPlanetThread.GetQueryRoute(planet.Id));

    /// <summary>
    /// Returns a query engine for the comments of a thread.
    /// Filters: parentId (omit for top-level), sort (top/new/old)
    /// </summary>
    public ModelQueryEngine<ThreadComment> GetCommentEngine(PlanetThread thread) =>
        new(thread.Planet.Node, ISharedThreadComment.GetQueryRoute(thread.PlanetId, thread.Id));

    //////////////
    // Threads //
    //////////////

    public async ValueTask<PlanetThread> FetchThreadAsync(long planetId, long threadId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId);
        if (planet is null)
            return null;

        if (!skipCache && planet.Threads.TryGet(threadId, out var cached))
            return cached;

        var response = await planet.Node.GetJsonAsync<PlanetThread>(
            ISharedPlanetThread.GetIdRoute(planetId, threadId), true);

        return response.Data?.Sync(_client);
    }

    public async Task<TaskResult<PlanetThread>> CreateThreadAsync(PlanetThread thread)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(thread.PlanetId);
        if (planet is null)
            return TaskResult<PlanetThread>.FromFailure("Planet not found.");

        var response = await planet.Node.PostAsyncWithResponse<PlanetThread>(
            ISharedPlanetThread.GetBaseRoute(thread.PlanetId), thread);

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    public async Task<TaskResult<PlanetThread>> EditThreadAsync(PlanetThread thread)
    {
        var response = await thread.Node.PutAsyncWithResponse<PlanetThread>(thread.IdRoute, thread);

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    public async Task<TaskResult> DeleteThreadAsync(PlanetThread thread)
    {
        var response = await thread.Node.DeleteAsync(thread.IdRoute);

        if (response.Success)
            thread.RemoveFromCache();

        return response;
    }

    public async Task<TaskResult<PlanetThread>> SetThreadLockAsync(PlanetThread thread, bool value)
    {
        var response = await thread.Node.PostAsyncWithResponse<PlanetThread>(
            ISharedPlanetThread.GetLockRoute(thread.PlanetId, thread.Id), value);

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    public async Task<TaskResult<PlanetThread>> SetThreadPinAsync(PlanetThread thread, bool value)
    {
        var response = await thread.Node.PostAsyncWithResponse<PlanetThread>(
            ISharedPlanetThread.GetPinRoute(thread.PlanetId, thread.Id), value);

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    ///////////
    // Boosts //
    ///////////

    public bool IsThreadBoosted(long threadId) => _boostedThreads.Contains(threadId);

    public bool IsCommentBoosted(long commentId) => _boostedComments.Contains(commentId);

    /// <summary>
    /// Toggles the current user's boost on a thread. Optimistically updates local state.
    /// </summary>
    public async Task<TaskResult<PlanetThread>> ToggleThreadBoostAsync(PlanetThread thread)
    {
        var route = ISharedPlanetThread.GetBoostRoute(thread.PlanetId, thread.Id);
        var boosting = !IsThreadBoosted(thread.Id);

        // Optimistic local update
        if (boosting)
            _boostedThreads.Add(thread.Id);
        else
            _boostedThreads.Remove(thread.Id);

        var response = boosting
            ? await thread.Node.PostAsyncWithResponse<PlanetThread>(route)
            : await DeleteWithResponseAsync(thread, route);

        if (response.Success)
        {
            response.Data?.Sync(_client);
        }
        else
        {
            // Roll back optimistic state
            if (boosting)
                _boostedThreads.Remove(thread.Id);
            else
                _boostedThreads.Add(thread.Id);
        }

        return response;
    }

    private async Task<TaskResult<PlanetThread>> DeleteWithResponseAsync(PlanetThread thread, string route)
    {
        var result = await thread.Node.DeleteAsync(route);
        if (!result.Success)
            return TaskResult<PlanetThread>.FromFailure(result.Message);

        var refreshed = await FetchThreadAsync(thread.PlanetId, thread.Id, skipCache: true);
        return TaskResult<PlanetThread>.FromData(refreshed);
    }

    /// <summary>
    /// Loads which of the given threads the current user has boosted into local state.
    /// </summary>
    public async Task LoadThreadBoostStateAsync(IEnumerable<PlanetThread> threads)
    {
        var byPlanet = threads
            .Where(x => x is not null)
            .GroupBy(x => x.PlanetId);

        foreach (var group in byPlanet)
        {
            var planet = await _client.PlanetService.FetchPlanetAsync(group.Key);
            if (planet is null)
                continue;

            var response = await planet.Node.PostAsyncWithResponse<List<long>>(
                ISharedPlanetThread.GetBoostLookupRoute(group.Key),
                new BoostLookupRequest { Ids = group.Select(x => x.Id).ToList() });

            if (!response.Success || response.Data is null)
                continue;

            foreach (var thread in group)
                _boostedThreads.Remove(thread.Id);

            foreach (var id in response.Data)
                _boostedThreads.Add(id);
        }
    }

    /// <summary>
    /// Loads which of the given comments the current user has boosted into local state.
    /// </summary>
    public async Task LoadCommentBoostStateAsync(PlanetThread thread, IEnumerable<ThreadComment> comments)
    {
        var ids = comments
            .Where(x => x is not null)
            .Select(x => x.Id)
            .ToList();

        if (ids.Count == 0)
            return;

        var response = await thread.Node.PostAsyncWithResponse<List<long>>(
            ISharedThreadComment.GetBoostLookupRoute(thread.PlanetId, thread.Id),
            new BoostLookupRequest { Ids = ids });

        if (!response.Success || response.Data is null)
            return;

        foreach (var id in ids)
            _boostedComments.Remove(id);

        foreach (var id in response.Data)
            _boostedComments.Add(id);
    }

    ///////////////
    // Comments //
    ///////////////

    /// <summary>
    /// Queries a page of comments for a thread. A null parentId returns top-level comments.
    /// Sort can be "top", "new", or "old".
    /// </summary>
    public async Task<ModelQueryResponse<ThreadComment>> QueryCommentsAsync(
        PlanetThread thread,
        long? parentId,
        string sort,
        int skip,
        int take)
    {
        var request = new Valour.Shared.Queries.QueryRequest()
        {
            Skip = skip,
            Take = take,
            Options = new Valour.Shared.Queries.QueryOptions()
            {
                Filters = new Dictionary<string, string>()
                {
                    ["sort"] = sort ?? "top"
                }
            }
        };

        if (parentId is not null)
            request.Options.Filters["parentId"] = parentId.Value.ToString();

        var response = await thread.Node.PostAsyncWithResponse<ModelQueryResponse<ThreadComment>>(
            ISharedThreadComment.GetQueryRoute(thread.PlanetId, thread.Id), request);

        if (!response.Success || response.Data is null)
            return new ModelQueryResponse<ThreadComment>() { Items = new List<ThreadComment>(), TotalCount = 0 };

        response.Data.Sync(_client);
        return response.Data;
    }

    public async ValueTask<ThreadComment> FetchCommentAsync(long planetId, long threadId, long commentId, bool skipCache = false)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(planetId);
        if (planet is null)
            return null;

        if (!skipCache && planet.ThreadComments.TryGet(commentId, out var cached))
            return cached;

        var response = await planet.Node.GetJsonAsync<ThreadComment>(
            ISharedThreadComment.GetIdRoute(planetId, threadId, commentId), true);

        return response.Data?.Sync(_client);
    }

    public async Task<TaskResult<ThreadComment>> CreateCommentAsync(ThreadComment comment)
    {
        var planet = await _client.PlanetService.FetchPlanetAsync(comment.PlanetId);
        if (planet is null)
            return TaskResult<ThreadComment>.FromFailure("Planet not found.");

        var response = await planet.Node.PostAsyncWithResponse<ThreadComment>(
            ISharedThreadComment.GetBaseRoute(comment.PlanetId, comment.ThreadId), comment);

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    public async Task<TaskResult<ThreadComment>> EditCommentAsync(ThreadComment comment)
    {
        var response = await comment.Node.PutAsyncWithResponse<ThreadComment>(comment.IdRoute, comment);

        if (response.Success)
            response.Data?.Sync(_client);

        return response;
    }

    public async Task<TaskResult> DeleteCommentAsync(ThreadComment comment)
    {
        // Comments tombstone rather than fully delete; the server broadcasts the update
        return await comment.Node.DeleteAsync(comment.IdRoute);
    }

    /// <summary>
    /// Toggles the current user's boost on a comment. Optimistically updates local state.
    /// </summary>
    public async Task<TaskResult<ThreadComment>> ToggleCommentBoostAsync(ThreadComment comment)
    {
        var route = ISharedThreadComment.GetBoostRoute(comment.PlanetId, comment.ThreadId, comment.Id);
        var boosting = !IsCommentBoosted(comment.Id);

        if (boosting)
            _boostedComments.Add(comment.Id);
        else
            _boostedComments.Remove(comment.Id);

        TaskResult<ThreadComment> response;
        if (boosting)
        {
            response = await comment.Node.PostAsyncWithResponse<ThreadComment>(route);
        }
        else
        {
            var deleteResult = await comment.Node.DeleteAsync(route);
            if (deleteResult.Success)
            {
                var refreshed = await FetchCommentAsync(comment.PlanetId, comment.ThreadId, comment.Id, skipCache: true);
                response = TaskResult<ThreadComment>.FromData(refreshed);
            }
            else
            {
                response = TaskResult<ThreadComment>.FromFailure(deleteResult.Message);
            }
        }

        if (response.Success)
        {
            response.Data?.Sync(_client);
        }
        else
        {
            if (boosting)
                _boostedComments.Remove(comment.Id);
            else
                _boostedComments.Add(comment.Id);
        }

        return response;
    }
}
