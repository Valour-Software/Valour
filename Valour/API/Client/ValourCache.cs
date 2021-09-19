using System.Collections.Concurrent;
using Valour.Api.Planets;
using Valour.Api.Users;
using Valour.Client.Categories;

namespace Valour.Api.Client;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

public static class ValourCache
{
    public static ConcurrentDictionary<ulong, PlanetChatChannel> Channels { get; set; }
    public static ConcurrentDictionary<ulong, PlanetCategory> Categories { get; set; }
    public static ConcurrentDictionary<ulong, PlanetMember> Members { get; set; }
    public static ConcurrentDictionary<(ulong, ulong), PlanetMember> Members_DualId { get; set; } 
    public static ConcurrentDictionary<ulong, Planet> Planets { get; set; }
    public static ConcurrentDictionary<ulong, PlanetRole> Roles { get; set; }
    public static ConcurrentDictionary<ulong, User> Users { get; set; }

    static ValourCache()
    {
        // Create cache containers
        Channels = new ConcurrentDictionary<ulong, PlanetChatChannel>();
        Categories = new ConcurrentDictionary<ulong, PlanetCategory>();
        Members = new ConcurrentDictionary<ulong, PlanetMember>();
        Members_DualId = new ConcurrentDictionary<(ulong, ulong), PlanetMember>();
        Planets = new ConcurrentDictionary<ulong, Planet>();
        Roles = new ConcurrentDictionary<ulong, PlanetRole>();
        Users = new ConcurrentDictionary<ulong, User>();
    }
}

