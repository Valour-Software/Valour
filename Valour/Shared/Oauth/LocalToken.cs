using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.Oauth
{
    /// <summary>
    /// The object for storing a token locally
    /// </summary>
    public class LocalToken
    {
        [JsonProperty]
        [JsonPropertyName("Token")]
        public string Token { get; set; }
    }
}
