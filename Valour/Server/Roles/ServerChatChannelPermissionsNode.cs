using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Server.Planets;
using Valour.Shared.Roles;

namespace Valour.Server.Roles
{
    public class ServerChatChannelPermissionsNode : ChatChannelPermissionsNode
    {
        
        [ForeignKey("Channel_Id")]
        [JsonIgnore]
        public virtual ServerPlanetChatChannel Channel { get; set; }

        [ForeignKey("Planet_Id")]
        [JsonIgnore]
        public virtual ServerPlanet Planet { get; set; }

        [ForeignKey("Role_Id")]
        [JsonIgnore]
        public virtual ServerPlanetRole Role { get; set; }
    }
}
