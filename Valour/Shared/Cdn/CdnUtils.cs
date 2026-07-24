using System.Net;
using System.Text.RegularExpressions;
using Valour.Shared.Models;

namespace Valour.Shared.Cdn
{
    public static class CdnUtils
    {
        public static bool IsSuccessStatusCode(HttpStatusCode statusCode)
        {
            var intStatus = (int)statusCode;
            return intStatus >= 200 && intStatus <= 299;
        }

        public static readonly HashSet<string> ImageSharpSupported = new()
        {
            "image/png",
            "image/jpeg",
            "image/jpg", // "image/jpg" is not a valid mime type, but we'll support it anyway
            "image/gif",
            "image/bmp",
            "image/tiff",
            "image/webp"
        };

        private static readonly HashSet<string> ExecutableUploadExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".scr", ".msi", ".msp", ".msix", ".appx", ".appxbundle",
            ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".wsf", ".wsh", ".hta",
            ".cpl", ".dll", ".sys", ".jar", ".apk", ".dmg", ".pkg", ".deb", ".rpm",
            ".run", ".appimage", ".sh", ".command", ".reg", ".lnk"
        };

        private static readonly HashSet<string> ExecutableUploadMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "application/x-msdownload",
            "application/x-msdos-program",
            "application/vnd.microsoft.portable-executable",
            "application/x-dosexec",
            "application/x-msi",
            "application/x-ms-installer",
            "application/java-archive",
            "application/vnd.android.package-archive",
            "application/x-apple-diskimage",
            "application/x-sh",
            "application/x-shellscript"
        };

        private static readonly HashSet<string> ActiveContentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".html", ".htm", ".xhtml", ".xht", ".shtml", ".svg", ".svgz",
            ".xml", ".xsl", ".xslt", ".mhtml", ".mht", ".htc"
        };

        private static readonly HashSet<string> ActiveContentMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "text/html",
            "application/xhtml+xml",
            "image/svg+xml",
            "text/xml",
            "application/xml",
            "application/xslt+xml",
            "multipart/related",
            "text/xsl"
        };

        /// <summary>
        /// Returns true when a browser would treat the upload as active content -
        /// HTML, SVG, or XML that can carry script. Serving one of these inline
        /// from a Valour host means attacker script runs on a Valour origin, so
        /// these are rejected at upload rather than relying on the serve headers
        /// alone.
        /// </summary>
        public static bool IsActiveContentUpload(string? fileName, string? contentType)
        {
            var extension = Path.GetExtension(Path.GetFileName(fileName ?? string.Empty).TrimEnd(' ', '.'));
            if (ActiveContentExtensions.Contains(extension))
                return true;

            var normalizedContentType = contentType?.Split(';', 2)[0].Trim();
            if (normalizedContentType is null)
                return false;

            if (ActiveContentMimeTypes.Contains(normalizedContentType))
                return true;

            // Any +xml subtype is XML, and XML can carry script via XSLT or
            // embedded namespaces, so treat the whole family as active.
            return normalizedContentType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true when an attachment is executable by its name, declared MIME type,
        /// or native executable signature. The signature check prevents a PE, ELF, or
        /// Mach-O binary from bypassing the filename rules by being renamed.
        /// </summary>
        public static bool IsExecutableUpload(
            string? fileName,
            string? contentType,
            ReadOnlySpan<byte> contentPrefix = default)
        {
            var normalizedFileName = Path.GetFileName(fileName ?? string.Empty);
            var alternateStreamIndex = normalizedFileName.IndexOf(':');
            if (alternateStreamIndex >= 0)
                normalizedFileName = normalizedFileName[..alternateStreamIndex];

            normalizedFileName = normalizedFileName.TrimEnd(' ', '.');
            var extension = Path.GetExtension(normalizedFileName);
            if (ExecutableUploadExtensions.Contains(extension))
                return true;

            var normalizedContentType = contentType?.Split(';', 2)[0].Trim();
            if (normalizedContentType is not null && ExecutableUploadMimeTypes.Contains(normalizedContentType))
                return true;

            if (contentPrefix.Length >= 2 && contentPrefix[0] == (byte)'M' && contentPrefix[1] == (byte)'Z')
                return true;

            if (contentPrefix.Length < 4)
                return false;

            // ELF and the common 32/64-bit, endian-swapped, and fat Mach-O magics.
            return contentPrefix[..4].SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }) ||
                   contentPrefix[..4].SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa, 0xce }) ||
                   contentPrefix[..4].SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa, 0xcf }) ||
                   contentPrefix[..4].SequenceEqual(new byte[] { 0xce, 0xfa, 0xed, 0xfe }) ||
                   contentPrefix[..4].SequenceEqual(new byte[] { 0xcf, 0xfa, 0xed, 0xfe }) ||
                   contentPrefix[..4].SequenceEqual(new byte[] { 0xca, 0xfe, 0xba, 0xbe });
        }
        
        public static readonly Regex UrlRegex = new Regex(@"(!\[\]\()?(http|https|)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([=a-zA-Z0-9\:\-\.\?\,\'\/\\\+&%\$#_]*)?([=a-zA-Z0-9\-\?\,\'\/\+&%\$#_]+)(\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        /// <summary>
        /// Map for attachments that do not have a file extension
        /// </summary>
        public static readonly Dictionary<string, MessageAttachmentType> VirtualAttachmentMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Video platforms
            { "youtube.com", MessageAttachmentType.YouTube },
            { "youtu.be", MessageAttachmentType.YouTube },
            { "music.youtube.com", MessageAttachmentType.YouTube },
            { "vimeo.com", MessageAttachmentType.Vimeo },
            { "twitch.tv", MessageAttachmentType.Twitch },
            { "tiktok.com", MessageAttachmentType.TikTok },

            // Social platforms
            { "twitter.com", MessageAttachmentType.Twitter },
            { "x.com", MessageAttachmentType.Twitter },
            { "reddit.com", MessageAttachmentType.Reddit },
            { "instagram.com", MessageAttachmentType.Instagram },
            { "bsky.app", MessageAttachmentType.Bluesky },

            // Music platforms
            { "open.spotify.com", MessageAttachmentType.Spotify },
            { "spotify.com", MessageAttachmentType.Spotify },
            { "soundcloud.com", MessageAttachmentType.SoundCloud },

            // Developer platforms
            { "github.com", MessageAttachmentType.GitHub },
            { "gist.github.com", MessageAttachmentType.GitHub },
        };

        /// <summary>
        /// Resolves the virtual attachment type for a host. This deployment's
        /// own hosts (from ValourHosts) get an inline thread preview card.
        /// </summary>
        public static bool TryGetVirtualAttachmentType(string host, out MessageAttachmentType type)
        {
            if (VirtualAttachmentMap.TryGetValue(host, out type))
                return true;

            if (ValourHosts.IsSelfHost(host))
            {
                type = MessageAttachmentType.ValourThread;
                return true;
            }

            return false;
        }

        public static bool IsVirtualAttachmentType(MessageAttachmentType type) =>
            VirtualAttachmentMap.ContainsValue(type);

        /// <summary>
        /// Third-party attachment sources that bypass Valour CDN
        /// </summary>
        public static HashSet<string> MediaBypassList = new()
        {
            "youtube.com",
            "cdn.discordapp.com",
            "vimeo.com",
            "tenor.com",
            "klipy.com",
            "i.imgur.com",
            "youtu.be",
            "pbs.twimg.com",
        };

        /// <summary>
        /// True if media from this host bypasses the Valour CDN proxy — either
        /// a trusted third-party source or this deployment's own hosts.
        /// Evaluated at call time so it respects configured hosts.
        /// </summary>
        public static bool IsMediaBypassHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;

            return MediaBypassList.Contains(host) ||
                   host.Equals(ValourHosts.ContentCdnHost, StringComparison.OrdinalIgnoreCase) ||
                   ValourHosts.IsSelfHost(host);
        }
        
        public static readonly Dictionary<string, string> ExtensionToMimeType = new(StringComparer.OrdinalIgnoreCase)
        {
            // Video
            {".3gp", "video/3gpp"},
            {".3g2", "video/3gpp2"},
            {".avi", "video/x-msvideo"},
            {".uvh", "video/vnd.dece.hd"},
            {".uvm", "video/vnd.dece.mobile"},
            {".uvu", "video/vnd.uvvu.mp4"},
            {".uvp", "video/vnd.dece.pd"},
            {".uvs", "video/vnd.dece.sd"},
            {".uvv", "video/vnd.dece.video"},
            {".fvt", "video/vnd.fvt"},
            {".f4v", "video/x-f4v"},
            {".flv", "video/x-flv"},
            {".fli", "video/x-fli"},
            {".h261", "video/h261"},
            {".h263", "video/h263"},
            {".h264", "video/h264"},
            {".jpm", "video/jpm"},
            {".jpgv", "video/jpeg"},
            {".m4v", "video/x-m4v"},
            {".asf", "video/x-ms-asf"},
            {".pyv", "video/vnd.ms-playready.media.pyv"},
            {".wm", "video/x-ms-wm"},
            {".wmx", "video/x-ms-wmx"},
            {".wmv", "video/x-ms-wmv"},
            {".wvx", "video/x-ms-wvx"},
            {".mj2", "video/mj2"},
            {".mxu", "video/vnd.mpegurl"},
            {".mpeg", "video/mpeg"},
            {".mp4", "video/mp4"},
            {".ogv", "video/ogg"},
            {".webm", "video/webm"},
            {".qt", "video/quicktime"},
            {".movie", "video/x-sgi-movie"},
            {".viv", "video/vnd.vivo"},
            {".mov", "video/quicktime"},

            // Audio
            {".adp", "audio/adpcm"},
            {".aac", "audio/x-aac"},
            {".aif", "audio/x-aiff"},
            {".uva", "audio/vnd.dece.audio"},
            {".eol", "audio/vnd.digital-winds"},
            {".dra", "audio/vnd.dra"},
            {".dts", "audio/vnd.dts"},
            {".dtshd", "audio/vnd.dts.hd"},
            {".rip", "audio/vnd.rip"},
            {".lvp", "audio/vnd.lucent.voice"},
            {".m3u", "audio/x-mpegurl"},
            {".pya", "audio/vnd.ms-playready.media.pya"},
            {".wma", "audio/x-ms-wma"},
            {".wax", "audio/x-ms-wax"},
            {".mid", "audio/midi"},
            {".mp3", "audio/mpeg"},
            {".mpga", "audio/mpeg"},
            {".mp4a", "audio/mp4"},
            {".ecelp4800", "audio/vnd.nuera.ecelp4800"},
            {".ecelp7470", "audio/vnd.nuera.ecelp7470"},
            {".ecelp9600", "audio/vnd.nuera.ecelp9600"},
            {".oga", "audio/ogg"},
            {".ogg", "audio/ogg"},
            {".weba", "audio/webm"},
            {".ram", "audio/x-pn-realaudio"},
            {".rmp", "audio/x-pn-realaudio-plugin"},
            {".au", "audio/basic"},
            {".wav", "audio/x-wav"},

            // Fonts
            {".woff2", "font/woff2"},
            {".woff", "font/woff"},
            {".ttf", "font/ttf"},
            {".otf", "font/otf"},
            
            // Images
            {".gif", "image/gif"},
            {".jpg", "image/jpg"},
            {".jpeg", "image/jpg"},
            {".png", "image/png"},
            {".svg", "image/svg+xml"},
            {".djvu", "image/vnd.djvu"},
            {".djv", "image/vnd.djvu"},
            {".tiff", "image/tiff"},
            {".tif", "image/tiff"},
            {".bmp", "image/bmp"},
            {".webp", "image/webp"},
            {".heif", "image/heif"},
            {".heic", "image/heic"},
            {".avif", "image/avif"},
            {".apng", "image/apng"},
            {".pjpeg", "image/jpeg"},
            {".pjp", "image/jpeg"},
            {".jpe", "image/jpeg"},
            {".jif", "image/jpeg"},
            {".jfif", "image/jpeg"},
            {".jfi", "image/jpeg"},
        };

        public static HashSet<string> MediaTypes = new HashSet<string>()
        {
            // Audio
            "audio/mpeg",
            "audio/x-ms-wma",
            "audio/vnd.rn-realaudio",
            "audio/x-wav",
            // Image
            "image/gif",
            "image/jpeg",
            "image/png",
            "image/tiff",
            "image/vnd.microsoft.icon",
            "image/x-icon",
            "image/vnd.djvu",
            "image/svg+xml",
            // Video
            "video/mpeg",
            "video/mp4",
            "video/quicktime",
            "video/x-ms-wmv",
            "video/x-msvideo",
            "video/x-flv",
            "video/webm",
        };

        public static HashSet<string> Image_Types = new HashSet<string>()
        {
            // Image
            "image/gif",
            "image/jpeg",
            "image/png",
            "image/tiff",
            "image/vnd.microsoft.icon",
            "image/x-icon",
            "image/vnd.djvu",
            "image/svg+xml",
        };
    }
}
