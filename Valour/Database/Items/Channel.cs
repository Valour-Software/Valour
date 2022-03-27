using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Database.Items.Planets.Channels
{
    public class Channel : Item, ISharedChannel
    {
        public string Name { get; set; }
        public int Position { get; set; }
        public string Description { get; set; }

        public override ItemType ItemType => ItemType.Channel;
    }
}
