using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared.Planets;

namespace Valour.Server.Planets
{
    public interface IServerChannelListItem
    {
        public ushort Position { get; set; }
        public ulong? Parent_Id { get; set; }
        public ulong Planet_Id { get; set; }
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ChannelListItemType ItemType { get; }

        public Task SetNameAsync(string name, ValourDB db);
        public Task SetDescriptionAsync(string desc, ValourDB db);
    }
}
