#nullable enable

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Valour.Client.Components.Threads.Display;
using Valour.Server.Cdn;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Server.Pages;

public class ThreadViewModel : PageModel
{
    private const int MaxComments = 300;

    private readonly ValourDb _db;
    private readonly ThreadService _threadService;
    private readonly CdnMemoryCache _cdnCache;

    public ThreadViewModel(ValourDb db, ThreadService threadService, CdnMemoryCache cdnCache)
    {
        _db = db;
        _threadService = threadService;
        _cdnCache = cdnCache;
    }

    [BindProperty(SupportsGet = true)]
    public long PlanetId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long ThreadId { get; set; }

    public Valour.Database.Planet? Planet { get; set; }
    public Models.PlanetThread? Thread { get; set; }

    public bool IsPrivate { get; set; }
    public string? ErrorMessage { get; set; }

    public string Snippet { get; set; } = string.Empty;
    public string? OgImage { get; set; }
    public string PlanetIcon { get; set; } = string.Empty;

    /// <summary>
    /// Data consumed by the shared display components (same ones the app uses)
    /// </summary>
    public StaticThreadPostData? Post { get; set; }
    public List<StaticCommentData> Comments { get; } = new();
    public int TotalComments { get; set; }

    public string RequestUrl => $"{Request.Scheme}://{Request.Host}{Request.Path}";
    public string AppLink => $"/planetthreads/{PlanetId}/{ThreadId}";

    public async Task<IActionResult> OnGetAsync()
    {
        Planet = await _db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == PlanetId);
        if (Planet is null)
        {
            ErrorMessage = "This planet doesn't exist.";
            return Page();
        }

        PlanetIcon = NormalizeUrl(ISharedPlanet.GetIconUrl(Planet, IconFormat.Webp128));

        if (!Planet.EnableThreads || !Planet.PublicThreads)
        {
            IsPrivate = true;
            return Page();
        }

        Thread = await _threadService.GetThreadAsync(ThreadId);
        if (Thread is null || Thread.PlanetId != PlanetId)
        {
            Thread = null;
            ErrorMessage = "This thread doesn't exist or was deleted.";
            return Page();
        }

        Snippet = PublicThreadPageHelpers.ToPlainSnippet(Thread.Content);

        Post = new StaticThreadPostData
        {
            Title = Thread.Title,
            TimeCreated = Thread.TimeCreated,
            Edited = Thread.EditedTime is not null,
            IsPinned = Thread.Id == Planet.PinnedThreadId,
            IsLocked = Thread.IsLocked,
            Nsfw = Thread.Nsfw,
            BoostCount = Thread.BoostCount,
            CommentCount = Thread.CommentCount,
            ContentHtml = PublicThreadPageHelpers.RenderMarkdown(Thread.Content),
            Attachments = await LoadAttachmentsAsync()
        };

        await LoadCommentsAndAuthorsAsync();

        return Page();
    }

    private async Task<List<StaticAttachmentData>> LoadAttachmentsAsync()
    {
        var result = new List<StaticAttachmentData>();

        if (Thread?.Attachments is null)
            return result;

        foreach (var attachment in Thread.Attachments)
        {
            if (attachment?.Location is null)
                continue;

            var url = await PublicThreadPageHelpers.TryGetSignedUrlAsync(_db, _cdnCache, attachment.Location);
            if (url is null)
                continue;

            result.Add(new StaticAttachmentData
            {
                Url = url,
                FileName = attachment.FileName ?? "Attachment",
                IsImage = attachment.Type == MessageAttachmentType.Image,
                IsVideo = attachment.Type == MessageAttachmentType.Video
            });

            OgImage ??= attachment.Type == MessageAttachmentType.Image ? url : null;
        }

        return result;
    }

    private async Task LoadCommentsAndAuthorsAsync()
    {
        var comments = await _db.ThreadComments
            .AsNoTracking()
            .Where(x => x.ThreadId == ThreadId)
            .OrderBy(x => x.TimeCreated)
            .Take(MaxComments)
            .ToListAsync();

        TotalComments = Thread!.CommentCount;

        // Resolve author names: planet nicknames take priority over user names
        var userIds = comments.Select(x => x.AuthorUserId)
            .Append(Thread.AuthorUserId)
            .Distinct()
            .ToList();

        var users = await _db.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        var memberIds = comments.Where(x => x.AuthorMemberId != null)
            .Select(x => x.AuthorMemberId!.Value)
            .ToList();

        if (Thread.AuthorMemberId is not null)
            memberIds.Add(Thread.AuthorMemberId.Value);

        var nicknames = await _db.PlanetMembers
            .AsNoTracking()
            .Where(x => memberIds.Contains(x.Id) && x.Nickname != null)
            .ToDictionaryAsync(x => x.Id, x => x.Nickname!);

        var primaryRoles = await PublicThreadPageHelpers.GetPrimaryRolesAsync(_db, PlanetId, memberIds);

        (string? Name, string? Color) GetRole(long? memberId)
        {
            if (memberId is not null && primaryRoles.TryGetValue(memberId.Value, out var role))
                return role;
            return (null, null);
        }

        string GetName(long userId, long? memberId)
        {
            if (memberId is not null && nicknames.TryGetValue(memberId.Value, out var nick))
                return nick;
            return users.TryGetValue(userId, out var user) ? user.Name : "Unknown";
        }

        string GetAvatar(long userId)
        {
            users.TryGetValue(userId, out var user);
            return NormalizeUrl(ISharedUser.GetAvatar(user, AvatarFormat.Webp64));
        }

        Post!.AuthorName = GetName(Thread.AuthorUserId, Thread.AuthorMemberId);
        Post.AuthorAvatarUrl = GetAvatar(Thread.AuthorUserId);
        (Post.AuthorRoleName, Post.AuthorRoleColor) = GetRole(Thread.AuthorMemberId);

        // Build the comment tree: top-level by boosts, replies chronologically
        var views = new Dictionary<long, (Models.ThreadComment Model, StaticCommentData Data)>();
        foreach (var dbComment in comments)
        {
            var model = Mapping.ThreadMapper.ToModel(dbComment);
            var (roleName, roleColor) = model.IsDeleted ? (null, null) : GetRole(model.AuthorMemberId);
            views[model.Id] = (model, new StaticCommentData
            {
                AuthorName = model.IsDeleted ? "[deleted]" : GetName(model.AuthorUserId, model.AuthorMemberId),
                AuthorAvatarUrl = model.IsDeleted ? null : GetAvatar(model.AuthorUserId),
                AuthorRoleName = roleName,
                AuthorRoleColor = roleColor,
                TimeCreated = model.TimeCreated,
                Edited = model.EditedTime is not null,
                Deleted = model.IsDeleted,
                BoostCount = model.BoostCount,
                ContentHtml = model.IsDeleted ? string.Empty : PublicThreadPageHelpers.RenderMarkdown(model.Content),
                Replies = new List<StaticCommentData>()
            });
        }

        foreach (var (model, data) in views.Values)
        {
            if (model.ParentCommentId is not null &&
                views.TryGetValue(model.ParentCommentId.Value, out var parent))
            {
                parent.Data.Replies.Add(data);
            }
        }

        Comments.AddRange(views.Values
            .Where(x => x.Model.ParentCommentId is null)
            .OrderByDescending(x => x.Model.BoostCount)
            .ThenBy(x => x.Model.TimeCreated)
            .Select(x => x.Data));
    }

    private static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "/" + url.TrimStart('/');
    }
}
