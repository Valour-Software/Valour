using Valour.Server.Cdn;
using Valour.Server.Cdn.Storage;
using Valour.Server.Database;
using Valour.Server.Pages;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class LinkPreviewApi
{
    /// <summary>
    /// Returns the preview tags for a shared app address, such as an invite,
    /// for hosts that serve the app as static files. The Cloudflare Pages
    /// worker written by cf-build places them in the host page. Addresses
    /// without a preview of their own return 204 and keep the generic tags.
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/linkpreview/head")]
    public static async Task<IResult> GetHeadAsync(
        string path,
        HttpContext context,
        AppLinkPreviewService previews)
    {
        context.Response.Headers.CacheControl = "public, max-age=300";

        var meta = await previews.GetAsync(path);
        return meta is null
            ? Results.NoContent()
            : Results.Content(meta.RenderHead(), "text/html; charset=utf-8");
    }

    /// <summary>
    /// Redirects to the first image of a public thread, for the image in the
    /// thread's link preview. Attachment addresses are signed and expire, and
    /// other apps keep a preview long after reading it, so the preview points
    /// here and each visit gets a fresh address. Age-restricted threads and
    /// images marked as spoilers have no preview image.
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/linkpreview/threads/{planetId}/{threadId}/image")]
    public static async Task<IResult> GetThreadImageAsync(
        long planetId,
        long threadId,
        HttpContext context,
        ValourDb db,
        ThreadService threadService,
        CdnMemoryCache cdnCache,
        CdnStorageProvider cdnStorage)
    {
        var planet = await db.Planets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planetId);
        if (planet is null || !planet.EnableThreads || !planet.PublicThreads || planet.Nsfw)
            return Results.NotFound();

        var thread = await threadService.GetThreadAsync(threadId);
        if (thread is null || thread.PlanetId != planetId || thread.Nsfw)
            return Results.NotFound();

        var image = PreviewImageOf(thread);
        if (image is null)
            return Results.NotFound();

        var url = await PublicThreadPageHelpers.TryGetSignedUrlAsync(db, cdnCache, cdnStorage, image.Location);
        if (url is null)
            return Results.NotFound();

        // Signed addresses last an hour.
        context.Response.Headers.CacheControl = "public, max-age=600";
        return Results.Redirect(url);
    }

    /// <summary>
    /// The attachment a thread's link preview shows, if any.
    /// </summary>
    public static Valour.Sdk.Models.MessageAttachment PreviewImageOf(PlanetThread thread) =>
        thread.Nsfw
            ? null
            : thread.Attachments?.FirstOrDefault(x =>
                x is { Type: MessageAttachmentType.Image, IsSpoiler: false, Missing: false } &&
                !string.IsNullOrWhiteSpace(x.Location));
}
