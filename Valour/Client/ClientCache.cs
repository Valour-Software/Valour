using System.Collections.Concurrent;
using Valour.Client.Categories;
using Valour.Client.Channels;
using Valour.Client.Planets;

namespace Valour.Client
{
    public static class ClientCache
    {
        public static ConcurrentDictionary<ulong, ClientPlanetChatChannel> Channels { get; set; }
        public static ConcurrentDictionary<ulong, ClientPlanetCategory> Categories { get; set; }
        public static ConcurrentDictionary<ulong, ClientPlanetMember> Members { get; set; }
        public static ConcurrentDictionary<ulong, ClientPlanet> Planets { get; set; }

        static ClientCache()
        {
            // Create cache containers
            Channels = new ConcurrentDictionary<ulong, ClientPlanetChatChannel>();
            Categories = new ConcurrentDictionary<ulong, ClientPlanetCategory>();
            Members = new ConcurrentDictionary<ulong, ClientPlanetMember>();
            Planets = new ConcurrentDictionary<ulong, ClientPlanet>();
        }
    }
}
