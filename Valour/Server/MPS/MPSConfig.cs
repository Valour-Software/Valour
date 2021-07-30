using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.MPS
{
    public class MPSConfig
    {
        public static MPSConfig Current;

        public MPSConfig()
        {
            Current = this;
        }

        [JsonProperty]
        public string Api_Key { get; set; }

        [JsonIgnore]
        public string Api_Key_Encoded { get; set; }
    }
}
