using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace Valour.Shared.Cdn
{
    public static class CdnUtils
    {
        public static Regex Url_Regex = new Regex(@"(!\[\]\()?(http|https|)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([=a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?([=a-zA-Z0-9\-\?\,\'\/\+&%\$#_]+)(\))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<string> Media_Bypass = new List<string>()
        {
            "https://youtube.com/watch",
            "https://cdn.discordapp.com/",
            "https://vimeo.com/",
            "https://tenor.com/",
            "https://i.imgur.com/",
            "https://youtu.be/",
            "https://cdn.valour.gg",
            "https://valour.gg",
            "https://pbs.twimg.com",
             
            "http://youtube.com/watch",
            "http://cdn.discordapp.com/",
            "http://vimeo.com/",
            "http://tenor.com/",
            "http://i.imgur.com/",
            "http://youtu.be/",
            "http://cdn.valour.gg",
            "http://valour.gg",
            "http://pbs.twimg.com",
        };

        public static HashSet<string> Media_Types = new HashSet<string>()
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
