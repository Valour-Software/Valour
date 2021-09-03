using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.Users
{
    /// <summary>
    /// Used to request a password recovery operation
    /// </summary>
    public class PasswordRecoveryRequest
    {
        [JsonPropertyName("Password")]
        public string Password {  get; set; }

        [JsonPropertyName("Code")]
        public string Code { get; set; }
    }
}
