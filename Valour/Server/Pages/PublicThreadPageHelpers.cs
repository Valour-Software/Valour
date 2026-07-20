#nullable enable annotations

using System.Text.RegularExpressions;
using Markdig;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Cdn;
using Valour.Server.Cdn.Api;
using Valour.Server.Cdn.Storage;
using Valour.Server.Database;
using Valour.Shared.Cdn;

namespace Valour.Server.Pages;

/// <summary>
/// Shared rendering helpers for the public (non-Blazor) thread pages
/// </summary>
public static partial class PublicThreadPageHelpers
{
    // DisableHtml escapes any raw HTML in the content, which keeps user
    // markdown safe to render server-side without a Blazor sanitizer.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    [GeneratedRegex("«@[mu]-([0-9]{1,20})»")]
    private static partial Regex UserMentionRegex();

    [GeneratedRegex("«@[rc]-([0-9]{1,20})»")]
    private static partial Regex OtherMentionRegex();

    [GeneratedRegex("«e-:([a-z0-9_]{2,32}):~([0-9]{1,20})»")]
    private static partial Regex EmojiRegex();

    /// <summary>
    /// Replaces Valour mention/emoji tokens with plain-text placeholders,
    /// since the public pages cannot resolve them client-side.
    /// </summary>
    public static string CleanTokens(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        content = UserMentionRegex().Replace(content, "@user");
        content = OtherMentionRegex().Replace(content, "@mention");
        content = EmojiRegex().Replace(content, ":$1:");
        return content;
    }

    public static string RenderMarkdown(string? content)
    {
        var cleaned = CleanTokens(content);
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        try
        {
            return Markdown.ToHtml(cleaned, Pipeline);
        }
        catch
        {
            return System.Net.WebUtility.HtmlEncode(cleaned);
        }
    }

    /// <summary>
    /// Plain-text snippet for og:description and feed previews
    /// </summary>
    public static string ToPlainSnippet(string? content, int maxLength = 200)
    {
        var cleaned = CleanTokens(content);
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        string text;
        try
        {
            text = Markdown.ToPlainText(cleaned, Pipeline);
        }
        catch
        {
            text = cleaned;
        }

        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= maxLength ? text : text[..maxLength].TrimEnd() + "\u2026";
    }

    /// <summary>
    /// Normalizes a media URL for use in server-rendered pages. Data URIs
    /// (e.g. generated planet icon SVGs) and absolute URLs pass through;
    /// bare asset paths get rooted.
    /// </summary>
    public static string NormalizeMediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url ?? string.Empty;

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return url;

        return "/" + url.TrimStart('/');
    }

    /// <summary>
    /// Resolves each member's primary (highest) role, the same role chat
    /// uses for name colors and role tags.
    /// </summary>
    public static async Task<Dictionary<long, (string Name, string? Color)>> GetPrimaryRolesAsync(
        ValourDb db, long planetId, IReadOnlyCollection<long> memberIds)
    {
        var result = new Dictionary<long, (string, string?)>();

        if (memberIds.Count == 0)
            return result;

        var roles = await db.PlanetRoles
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .OrderBy(x => x.Position)
            .ToListAsync();

        if (roles.Count == 0)
            return result;

        var members = await db.PlanetMembers
            .AsNoTracking()
            .Where(x => memberIds.Contains(x.Id))
            .ToListAsync();

        foreach (var member in members)
        {
            // Roles are ordered by position; the first one held is primary
            foreach (var role in roles)
            {
                if (member.RoleMembership.HasRole(role.FlagBitIndex))
                {
                    result[member.Id] = (role.Name, role.Color);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves a CDN attachment location to a directly-loadable URL.
    /// Bucket content gets a pre-signed URL; proxy/Tenor URLs pass through.
    /// </summary>
    public static async Task<string?> TryGetSignedUrlAsync(ValourDb db, CdnMemoryCache cache, CdnStorageProvider storage, string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;

        if (location.StartsWith("https://media.tenor.com", StringComparison.OrdinalIgnoreCase) ||
            KlipyMediaUrls.IsAllowed(location) ||
            location.Contains("proxy/", StringComparison.OrdinalIgnoreCase))
            return location;

        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return null;

        var path = uri.AbsolutePath.TrimStart('/');
        if (!path.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
            return null;

        var bucketItemId = path["content/".Length..];

        try
        {
            var bucketItem = await db.CdnBucketItems.FindAsync(bucketItemId);
            if (bucketItem is null)
                return null;

            return await ContentApi.GetSignedUrlAsync(cache, storage, bucketItem);
        }
        catch
        {
            return null;
        }
    }
}
