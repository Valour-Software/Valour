﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Shared.Planets;
using Valour.Shared.Users;

namespace Valour.Shared.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */


    /// <summary>
    /// This represents a user within a planet and is used to represent membership
    /// </summary>
    public class PlanetMember
    {
        /// <summary>
        /// The Id of this member object
        /// </summary>
        [Key]
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        /// <summary>
        /// The user within the planet
        /// </summary>
        [JsonPropertyName("User_Id")]
        public ulong User_Id { get; set; }

        /// <summary>
        /// The planet the user is within
        /// </summary>
        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The name to be used within the planet
        /// </summary>
        [JsonPropertyName("Nickname")]
        public string Nickname { get; set; }

        /// <summary>
        /// The pfp to be used within the planet
        /// </summary>
        [JsonPropertyName("Member_Pfp")]
        public string Member_Pfp { get; set; }
    }

    /// <summary>
    /// This class exists so the server can pass extra data to the client when needed
    /// </summary>
    public class PlanetMemberInfo
    {
        [JsonPropertyName("Member")]
        public PlanetMember Member { get; set; }

        [JsonPropertyName("RoleIds")]
        public IEnumerable<ulong> RoleIds { get; set; }

        [JsonPropertyName("User")]
        public User User { get; set; }
    }
}
