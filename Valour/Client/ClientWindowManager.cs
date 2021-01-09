using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Channels;

namespace Valour.Client
{
    /// <summary>
    /// This tracks and manages the open windows for the client
    /// </summary>
    public static class ClientWindowManager
    {
        private static List<ClientWindow> OpenWindows = new List<ClientWindow>();

        public static void AddWindow(ClientWindow window)
        {
            window.Index = OpenWindows.Count;
            OpenWindows.Add(window);
        }

        public static int GetWindowCount()
        {
            return OpenWindows.Count;
        }

        public static ClientWindow GetWindow(int index)
        {
            if (index > OpenWindows.Count - 1)
            {
                return null;
            }

            return OpenWindows[index];
        }

        public static IEnumerable<ClientWindow> GetWindows()
        {
            return OpenWindows;
        }

        public static void ClearWindows()
        {
            OpenWindows.Clear();
        }

        public static void SetWindow(int index, ClientWindow window)
        {
            window.Index = index;
            OpenWindows.RemoveAt(index);
            OpenWindows.Insert(index, window);
        }
    }

    public class ClientWindow
    {
        /// <summary>
        /// The index of this window
        /// </summary>
        public int Index { get; set; }

        public ClientWindow(int index)
        {
            this.Index = index;
        }
    }

    public class HomeWindow : ClientWindow
    {
        public HomeWindow(int index) : base(index)
        {
            
        }
    }

    public class ChatChannelWindow : ClientWindow
    {
        /// <summary>
        /// The channel this window represents
        /// </summary>
        public PlanetChatChannel Channel { get; set; }

        public ChatChannelWindow(int index, PlanetChatChannel channel) : base(index)
        {
            this.Channel = channel;
        }
    }
}
