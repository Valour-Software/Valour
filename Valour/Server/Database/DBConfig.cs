using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Database
{
    public class DBConfig
    {
        public static DBConfig instance;

        [JsonProperty]
        public string Host { get; set; }

        [JsonProperty]
        public string Password { get; set; }

        [JsonProperty]
        public string Username { get; set; }

        [JsonProperty]
        public string Database { get; set; }
    }
}
