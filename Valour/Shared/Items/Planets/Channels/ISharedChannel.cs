using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Items.Planets.Channels;

public interface ISharedChannel : ISharedItem
{
    string Name { get; set; }
    int Position { get; set; }
    string Description { get; set; }
    bool InheritsPerms { get; set; }
}
