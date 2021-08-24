using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.Users
{
    public class Referral
    {
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        [JsonPropertyName("User_Id")]
        public ulong User_Id { get; set; }

        [JsonPropertyName("Referrer_Id")]
        public ulong Referrer_Id { get; set; }
    }
}
