using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Items.Planets.Channels;

public interface ISharedChannel : ISharedItem
{
    DateTime TimeLastActive { get; set; }
    string State { get; set; }
}
