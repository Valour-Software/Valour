using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebPush;

namespace Valour.Server.Notifications
{
    public class VapidConfig
    {
        public static VapidConfig Current;
        private static VapidDetails _details;

        public VapidConfig()
        {
            Current = this;
        }

        public VapidDetails GetDetails()
        {
            if (_details == null)
            {
                _details = new VapidDetails(Subject, PublicKey, PrivateKey);
            }

            return _details; 
        }

        [JsonProperty]
        public string Subject { get; set; }

        [JsonProperty]
        public string PublicKey { get; set; }

        [JsonProperty]
        public string PrivateKey { get; set; }
    }
}
