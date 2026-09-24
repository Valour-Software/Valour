using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;
using Valour.Shared.Cdn;

namespace Valour.Server.Cdn;

/// <summary>
/// Decides how uploaded or proxied bytes are presented to a browser. The file
/// name and MIME type come from the uploader or a third-party URL, so only
/// media types a browser displays without running script are served inline.
/// Everything else is served as a download, and unknown or active types are
/// relabelled application/octet-stream.
/// </summary>
public static partial class CdnServePolicy
{
    public const string FallbackFileName = "file";
    public const string OpaqueContentType = "application/octet-stream";

    private const int MaxFileNameLength = 255;

    private static readonly HashSet<string> TopLevelTypes = new(StringComparer.Ordinal)
    {
        "application", "audio", "font", "image", "message", "model", "multipart", "text", "video"
    };

    private static readonly HashSet<string> InlineImageTypes = new(StringComparer.Ordinal)
    {
        "image/png", "image/jpeg", "image/jpg", "image/pjpeg", "image/gif", "image/webp", "image/avif",
        "image/apng", "image/bmp", "image/tiff", "image/heic", "image/heif", "image/x-icon",
        "image/vnd.microsoft.icon"
    };

    public readonly record struct ServeHeaders(string ContentType, string ContentDisposition, bool Inline);

    /// <summary>
    /// Returns the Content-Type and Content-Disposition to serve for a stored
    /// file name and MIME type.
    /// </summary>
    public static ServeHeaders Resolve(string fileName, string mimeType)
    {
        var safeFileName = NormalizeFileName(fileName);
        var contentType = NormalizeMimeType(mimeType);

        if (contentType is null ||
            CdnUtils.IsActiveContentUpload(fileName, contentType) ||
            CdnUtils.IsActiveContentUpload(safeFileName, contentType))
        {
            contentType = OpaqueContentType;
        }

        var inline = IsInlineMediaType(contentType);

        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        disposition.SetHttpFileName(safeFileName);

        return new ServeHeaders(contentType, disposition.ToString(), inline);
    }

    /// <summary>
    /// Writes the disposition and nosniff headers. nosniff stops a browser from
    /// guessing HTML out of a body whose declared type it does not recognize.
    /// </summary>
    public static void Apply(HttpResponse response, ServeHeaders headers)
    {
        response.Headers.ContentDisposition = headers.ContentDisposition;
        response.Headers.XContentTypeOptions = "nosniff";
    }

    public static bool IsInlineMediaType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        return InlineImageTypes.Contains(contentType) ||
               contentType.StartsWith("audio/", StringComparison.Ordinal) ||
               contentType.StartsWith("video/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Lowercases a MIME type and removes parameters. Returns null unless it is
    /// a syntactically valid type under a registered top-level type.
    /// </summary>
    public static string NormalizeMimeType(string mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return null;

        var normalized = mimeType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!MimeTypeRegex().IsMatch(normalized))
            return null;

        var topLevel = normalized[..normalized.IndexOf('/')];
        return TopLevelTypes.Contains(topLevel) ? normalized : null;
    }

    /// <summary>
    /// Returns a display-safe file name: no directory parts, control characters,
    /// quotes, or backslashes, and never empty.
    /// </summary>
    public static string NormalizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return FallbackFileName;

        var lastSeparator = fileName.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
            fileName = fileName[(lastSeparator + 1)..];

        var builder = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            if (c is '"' or '\\' || char.IsControl(c))
                continue;

            builder.Append(c);
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0 || cleaned.Trim('.').Length == 0)
            return FallbackFileName;

        if (cleaned.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(cleaned);
            if (extension.Length > 16)
                extension = string.Empty;

            cleaned = cleaned[..(MaxFileNameLength - extension.Length)] + extension;
        }

        return cleaned;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9!#$&^_.+-]{0,126}/[a-z0-9][a-z0-9!#$&^_.+-]{0,126}$")]
    private static partial Regex MimeTypeRegex();
}
