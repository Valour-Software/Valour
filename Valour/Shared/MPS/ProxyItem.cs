using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.MPS.Proxy
{
    public class ProxyItem
    {
        /// <summary>
        /// The id of proxied items are sha256 hashes of the original url
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The original url fed to the proxy server
        /// </summary>
        [JsonPropertyName("origin_url")]
        public string Origin_Url { get; set; }

        /// <summary>
        /// The type of content at the origin
        /// </summary>
        [JsonPropertyName("mime_type")]
        public string Mime_Type { get; set; }

        /// <summary>
        /// The url for the proxied item
        /// </summary>
        [JsonIgnore]
        public string Url => $"https://vmps.valour.gg/Proxy/{Id}";
    }
}
