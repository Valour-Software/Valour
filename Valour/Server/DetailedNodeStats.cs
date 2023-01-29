using System.Collections.Concurrent;
using Valour.Database;

namespace Valour.Server.Database.Nodes
{
    public class DetailedNodeStats : NodeStats
    {
        // Map of groups to joined identities 
        public ConcurrentDictionary<string, List<string>> GroupConnections { get; set; }

        // Map of groups to user ids
        public ConcurrentDictionary<string, List<long>> GroupUserIds { get; set; }

        // Map of connection to joined groups
        public ConcurrentDictionary<string, List<string>> ConnectionGroups { get; set; }

        // Map of user id to joined groups
        public ConcurrentDictionary<long, List<string>> UserIdGroups { get; set; }
    }
}
