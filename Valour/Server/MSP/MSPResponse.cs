using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.MSP
{
    public class MSPResponse
    {
        [JsonProperty("status")]
        public int Status { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("URL")]
        public string Url { get; set; }

        [JsonProperty("is-media")]
        public bool Is_Media { get; set; }
    }
}
