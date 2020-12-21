using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared.Channels
{
    /// <summary>
    /// Defines common functionality between all chat channels
    /// </summary>
    public interface IChatChannel
    {
        /// <summary>
        /// The Id of this channel
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// The name of this channel
        /// </summary>
        public string Name { get; set; }
    }
}
