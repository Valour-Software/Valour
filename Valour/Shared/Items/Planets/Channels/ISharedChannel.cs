using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Items.Planets.Channels
{
    public interface ISharedChannel
    {
        ulong Id { get; set; }
        string Name { get; set; }
        int Position { get; set; }
        string Description { get; set; }
        bool InheritsPerms { get; set; }
    }
}
