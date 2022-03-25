using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Items;

namespace Valour.Api.Items
{
    public abstract class Item : ISharedItem
    {
        public ulong Id { get; set; }
        public abstract ItemType ItemType { get; }
    }
}
