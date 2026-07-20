#nullable enable annotations

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Valour.Client.Components.Threads.Display;
using Valour.Config.Configs;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;
using Valour.Shared.Queries;

namespace Valour.Server.Pages;

public class PlanetThreadsModel : PageModel
{
    public const int PageSize = 20;

    private readonly ValourDb _db;
    private readonly ThreadService _threadService;

    public PlanetThreadsModel(ValourDb db, ThreadService threadService)
    {
        _db = db;
        _threadService = threadService;
    }

    [BindProperty(SupportsGet = true)]
    public long PlanetId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public Valour.Database.Planet? Planet { get; set; }
    public bool IsPrivate { get; set; }
    public string? ErrorMessage { get; set; }
    public string PlanetIcon { get; set; } = string.Empty;

    public List<ThreadListItem> Threads { get; } = new();
    public int TotalCount { get; set; }
    public bool HasNextPage => P * PageSize < TotalCount;
    public bool HasPrevPage => P > 1;

    public string RequestUrl => $"{HostingConfig.Current.ThreadsBaseUrl}/{PlanetId}";
    public string AppLink => $"{HostingConfig.Current.AppBaseUrl}/planetthreads/{PlanetId}";

    /// <summary>
    /// Clean public thread URL on the threads subdomain for a given thread id.
    /// </summary>
    public string ThreadLink(long threadId) => $"{HostingConfig.Current.ThreadsBaseUrl}/{PlanetId}/{threadId}";

    public class ThreadListItem
    {
        public long ThreadId { get; init; }

        /// <summary>
        /// Data for the shared display component (same one the app feed uses)
        /// </summary>
        public StaticThreadPostData Post { get; init; } = null!;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (P < 1)
            P = 1;

        Planet = await _db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == PlanetId);
        if (Planet is null)
        {
            ErrorMessage = "This planet doesn't exist.";
            return Page();
        }

        PlanetIcon = PublicThreadPageHelpers.NormalizeMediaUrl(ISharedPlanet.GetIconUrl(Planet, IconFormat.Webp128));

        if (!Planet.EnableThreads || !Planet.PublicThreads)
        {
            IsPrivate = true;
            return Page();
        }

        var response = await _threadService.QueryPlanetThreadsAsync(PlanetId, new QueryRequest
        {
            Skip = (P - 1) * PageSize,
            Take = PageSize,
            Options = new QueryOptions
            {
                Filters = new Dictionary<string, string> { ["sort"] = "hot" }
            }
        });

        TotalCount = response.TotalCount;

        var userIds = response.Items.Select(x => x.AuthorUserId).Distinct().ToList();
        var users = await _db.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var memberIds = response.Items
            .Where(x => x.AuthorMemberId != null)
            .Select(x => x.AuthorMemberId!.Value)
            .ToList();

        var nicknames = await _db.PlanetMembers
            .AsNoTracking()
            .Where(x => memberIds.Contains(x.Id) && x.Nickname != null)
            .ToDictionaryAsync(x => x.Id, x => x.Nickname!);

        var primaryRoles = await PublicThreadPageHelpers.GetPrimaryRolesAsync(_db, PlanetId, memberIds);

        foreach (var thread in response.Items)
        {
            users.TryGetValue(thread.AuthorUserId, out var user);

            var name = thread.AuthorMemberId is not null && nicknames.TryGetValue(thread.AuthorMemberId.Value, out var nick)
                ? nick
                : user?.Name ?? "Unknown";

            (string? roleName, string? roleColor) = thread.AuthorMemberId is not null &&
                primaryRoles.TryGetValue(thread.AuthorMemberId.Value, out var role)
                    ? role
                    : (null, null);

            // NSFW previews hide their content on the public list
            var snippet = thread.Nsfw ? null : PublicThreadPageHelpers.ToPlainSnippet(thread.Content, 180);

            Threads.Add(new ThreadListItem
            {
                ThreadId = thread.Id,
                Post = new StaticThreadPostData
                {
                    Title = thread.Title,
                    AuthorName = name,
                    AuthorAvatarUrl = PublicThreadPageHelpers.NormalizeMediaUrl(ISharedUser.GetAvatar(user, AvatarFormat.Webp64)),
                    AuthorRoleName = roleName,
                    AuthorRoleColor = roleColor,
                    TimeCreated = thread.TimeCreated,
                    IsPinned = thread.Id == Planet.PinnedThreadId,
                    IsLocked = thread.IsLocked,
                    Nsfw = thread.Nsfw,
                    BoostCount = thread.BoostCount,
                    CommentCount = thread.CommentCount,
                    ContentHtml = string.IsNullOrWhiteSpace(snippet)
                        ? null
                        : "<p>" + System.Net.WebUtility.HtmlEncode(snippet) + "</p>"
                }
            });
        }

        return Page();
    }

}
