using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.MSP
{
    public class MSPConfig
    {
        public static MSPConfig Current;

        public MSPConfig()
        {
            Current = this;
        }

        [JsonProperty]
        public string Api_Key { get; set; }
    }
}
