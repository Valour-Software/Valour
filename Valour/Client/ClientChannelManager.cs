using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Channels;

namespace Valour.Client
{
    /// <summary>
    /// This tracks and manages the open channels for the client
    /// </summary>
    public static class ClientChannelManager
    {
        private static List<IChatChannel> OpenChatChannels = new List<IChatChannel>();

        public static void AddOpenChatChannel(IChatChannel channel)
        {
            OpenChatChannels.Add(channel);
        }

        public static int GetOpenChatChannelCount()
        {
            return OpenChatChannels.Count;
        }

        public static IChatChannel GetOpenChatChannel(int index)
        {
            if (index > OpenChatChannels.Count - 1)
            {
                return null;
            }

            return OpenChatChannels[index];
        }

        public static IEnumerable<IChatChannel> GetOpenChatChannels()
        {
            return OpenChatChannels;
        }
    }
}
