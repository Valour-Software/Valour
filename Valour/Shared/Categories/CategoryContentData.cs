using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Shared.Planets;

namespace Valour.Shared.Categories
{
    /// <summary>
    /// This class allows for information about item contents and ordering within a category
    /// to be easily sent to the server
    /// </summary>
    public class CategoryContentData
    {
        public ulong Id { get; set; }
        public ushort Position { get; set; }
        public ChannelListItemType ItemType { get; set; }
    }
}
