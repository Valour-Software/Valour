using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Valour.Server.MPS.Proxy;

namespace Valour.Server.MPS
{
    public static class MPSManager
    {
        public static HttpClient Http = new HttpClient();

        public const string MSP_PROXY = "https://vmps.valour.gg/Proxy/SendUrl";

        public static async Task<ProxyResponse> GetProxy(string url)
        {
            string encoded_url = HttpUtility.UrlEncode(url);

            string encoded_key = HttpUtility.UrlEncode(MPSConfig.Current.Api_Key);

            var response = await Http.PostAsync($"{MSP_PROXY}?auth={encoded_key}&url={encoded_url}", null);

            ProxyResponse data = JsonSerializer.Deserialize<ProxyResponse>(await response.Content.ReadAsStringAsync());

            return data;
        }

        public static Regex Url_Regex = new Regex(@"(http|https|)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([=a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?([=a-zA-Z0-9\-\?\,\'\/\+&%\$#_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<string> Media_Bypass = new List<string>()
        {
            "https://youtube.com/watch",
            "https://cdn.discordapp.com/",
            "https://vimeo.com/",
            "https://tenor.com/",
            "https://i.imgur.com/",
            "https://youtu.be/",
            "https://vmps.valour.gg",
            "https://valour.gg",
            "https://pbs.twimg.com",
             
            "http://youtube.com/watch",
            "http://cdn.discordapp.com/",
            "http://vimeo.com/",
            "http://tenor.com/",
            "http://i.imgur.com/",
            "http://youtu.be/",
            "http://vmps.valour.gg",
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

        public static async Task<string> HandleUrls(string content)
        {
            MatchCollection matches = Url_Regex.Matches(content);

            foreach (Match match in matches)
            {
                bool bypass = false;

                foreach (string s in Media_Bypass)
                {
                    if (match.Value.ToLower().Replace("www.", "").StartsWith(s))
                    {
                        bypass = true;
                        content = content.Replace(match.Value, $"![]({match.Value})");
                        break;
                    }
                }

                if (!bypass)
                {
                    ProxyResponse proxied = await GetProxy(match.Value);

                    if (Media_Types.Contains(proxied.Item.Mime_Type))
                    {
                        content = content.Replace(match.Value, $"![]({proxied.Item.Url})");
                    }
                }
            }

            return content;
        }
    }
}
