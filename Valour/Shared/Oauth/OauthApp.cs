using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Valour.Shared.Oauth
{
    /*  Valour - A free and secure chat client
    *  Copyright (C) 2021 Vooper Media LLC
    *  This program is subject to the GNU Affero General Public license
    *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
    */

    /// <summary>
    /// Oauth apps allow an organization or person to issue tokens on behalf of a user
    /// which can be easily tracked and revoked
    /// </summary>
    public class OauthApp
    {
        /// <summary>
        /// The ID of the app
        /// </summary>
        [Key]
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        /// <summary>
        /// The secret key for the app
        /// </summary>
        [JsonPropertyName("Secret")]
        public string Secret { get; set; }

        /// <summary>
        /// The ID of the user that created this app
        /// </summary>
        [JsonPropertyName("Owner_Id")]
        public ulong Owner_Id { get; set; }

        /// <summary>
        /// The amount of times this app has been used
        /// </summary>
        [JsonPropertyName("Uses")]
        public int Uses { get; set; }

        /// <summary>
        /// The public name for this app
        /// </summary>
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        /// <summary>
        /// The image used to represent the app
        /// </summary>
        [JsonPropertyName("Image_Url")]
        public string Image_Url { get; set; }
    }
}
