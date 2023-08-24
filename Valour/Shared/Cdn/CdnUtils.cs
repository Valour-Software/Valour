using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Valour.Shared.Models;

namespace Valour.Shared.Cdn
{
    public static class CdnUtils
    {
        public static readonly Regex UrlRegex = new Regex(@"(!\[\]\()?(http|https|)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([=a-zA-Z0-9\:\-\.\?\,\'\/\\\+&%\$#_]*)?([=a-zA-Z0-9\-\?\,\'\/\+&%\$#_]+)(\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        /// <summary>
        /// Map for attachments that do not have a file extension
        /// </summary>
        public static readonly Dictionary<string, MessageAttachmentType> VirtualAttachmentMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "youtube.com", MessageAttachmentType.YouTube },
            { "youtu.be", MessageAttachmentType.YouTube },
            { "vimeo.com", MessageAttachmentType.Vimeo },
            { "twitch.tv", MessageAttachmentType.Twitch },
            { "twitter.com", MessageAttachmentType.Twitter },
            { "x.com", MessageAttachmentType.Twitter },
            { "reddit.com", MessageAttachmentType.Reddit },
        };

        /// <summary>
        /// Set for attachment sources that bypass Valour CDN
        /// </summary>
        public static HashSet<string> MediaBypassList = new()
        {
            "youtube.com",
            "cdn.discordapp.com",
            "vimeo.com",
            "tenor.com",
            "i.imgur.com",
            "youtu.be",
            "cdn.valour.gg",
            "valour.gg",
            "pbs.twimg.com",
        };
        
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
