using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Server.MPS.Proxy
{
    public class ProxyResponse
    {
        [JsonPropertyName("item")]
        public ProxyItem Item { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("error")]
        public string Error { get; set; }

        /// <summary>
        /// Returns true if the status code is OK
        /// </summary>
        [JsonIgnore]
        public bool Success => Status == 200;
    }
}
