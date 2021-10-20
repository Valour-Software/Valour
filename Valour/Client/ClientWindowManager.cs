using Valour.Api.Client;
using Valour.Api.Messages;
using Valour.Api.Planets;
using Valour.Client.Messages;
using Valour.Client.Planets;
using Valour.Client.Shared.Windows.PlanetChannelWindow;

namespace Valour.Client
{
    /// <summary>
    /// This tracks and manages the open windows for the client
    /// </summary>
    public class ClientWindowManager
    {
        public Planet FocusedPlanet { get; private set; }

        private List<ClientWindow> OpenWindows = new();

        private List<ChatChannelWindow> OpenChatWindows = new();

        private ClientWindow SelectedWindow;

        public Func<Task> OnWindowSelect;

        public event Func<Task> OnChannelWindowUpdate;

        public event Func<Planet, Task> OnPlanetFocused;

        public static ClientWindowManager Instance;

        public ClientWindowManager()
        {
            Instance = this;
            ValourClient.HubConnection.Reconnected += OnSignalRReconnect;
            ValourClient.OnMessageRecieve += OnMessageRecieved;
        }

        public async Task SetFocusedPlanet(Planet planet)
        {
            if (planet is null)
            {
                if (FocusedPlanet is null)
                    return;
            }
            else
            {
                if (FocusedPlanet is not null)
                    if (planet.Id == FocusedPlanet.Id)
                        return;
            }

            FocusedPlanet = planet;

            // Ensure focused planet is open
            await ValourClient.OpenPlanet(planet);

            if (planet is null)
                Console.WriteLine("Set current planet to null");
            else
                Console.WriteLine($"Set focused planet to {planet.Name} ({planet.Id})");

            await OnPlanetFocused?.Invoke(planet);
        }

        public async Task OnMessageRecieved(PlanetMessage in_message)
        {

            // Create client wrapper
            ClientPlanetMessage message = new ClientPlanetMessage(in_message);

            if (!OpenChatWindows.Any(x => x.Channel.Id == message.Channel_Id))
            {
                Console.WriteLine($"Error: Recieved message for closed channel ({message.Channel_Id})");
                return;
            }

            foreach (var window in OpenChatWindows.Where(x => x.Channel.Id == message.Channel_Id))
            {
                await window.Component.OnRecieveMessage(message);
            }

        }

        public async Task RefreshOpenedChannels()
        {
            await OnChannelWindowUpdate.Invoke();
        }

        public async Task OnSignalRReconnect(string data)
        {
            foreach (ChatChannelWindow window in OpenWindows)
            {
                if (window is ChatChannelWindow)
                {
                    await window.Component.SetupNewChannelAsync();
                }
            }
        }

        /// <summary>
        /// Swaps the channel a chat channel window is showing
        /// </summary>
        public async Task SwapWindowChannel(ChatChannelWindow window, Channel newChannel)
        {
            // Already that channel
            if (window.Channel.Id == newChannel.Id)
                return;

            Console.WriteLine("Swapping chat channel " + window.Channel.Name + " for " + newChannel.Name);

            if (!(OpenChatWindows.Any(x => x.Index != window.Index && x.Channel.Id == newChannel.Id)))
            {
                await ValourClient.CloseChannel(window.Channel);
            }

            window.Channel = newChannel;

            await ValourClient.OpenChannel(newChannel);
        }

        public async Task SetSelectedWindow(int index)
        {
            await SetSelectedWindow(OpenWindows[index]);
        }

        public async Task SetSelectedWindow(ClientWindow window)
        {
            if (window == null || (SelectedWindow == window)) return;

            SelectedWindow = window;

            Console.WriteLine($"Set active window to {window.Index}");

            if (OnWindowSelect != null)
            {
                Console.WriteLine($"Invoking window change event");
                await OnWindowSelect?.Invoke();
            }
        }

        public ClientWindow GetSelectedWindow()
        {
            return SelectedWindow;
        }

        public void AddWindow(ClientWindow window)
        {
            window.Index = OpenWindows.Count;
            OpenWindows.Add(window);

            Console.WriteLine("Added window " + window.Index);

            ForceChatRefresh();
        }

        public int GetWindowCount()
        {
            return OpenWindows.Count;
        }

        public ClientWindow GetWindow(int index)
        {
            if (index > OpenWindows.Count - 1)
            {
                return null;
            }

            return OpenWindows[index];
        }

        public IEnumerable<ClientWindow> GetWindows()
        {
            return OpenWindows;
        }

        public void ClearWindows()
        {
            foreach (ClientWindow window in OpenWindows)
            {
                window.OnClosed();
            }

            OpenWindows.Clear();
        }

        public void SetWindow(int index, ClientWindow window)
        {
            if (OpenWindows[index] == window)
            {
                return;
            }

            window.Index = index;
            CloseWindow(index);
            OpenWindows.Insert(index, window);

            ForceChatRefresh();
        }

        public void CloseWindow(int index)
        {
            OpenWindows[index].OnClosed();
            OpenWindows.RemoveAt(index);

            int newInd = 0;

            foreach (ClientWindow window in OpenWindows)
            {
                window.Index = newInd;
                newInd++;
            }

            //ForceChatRefresh();
        }

        public void ForceChatRefresh()
        {
            foreach (ClientWindow window in OpenWindows)
            {
                ChatChannelWindow chat = window as ChatChannelWindow;

                if (chat != null && chat.Component != null && chat.Component.MessageHolder != null)
                {
                    // Force window refresh
                    chat.Component.MessageHolder.ForceRefresh();
                }
            }
        }
    }

    public class ClientWindow
    {
        /// <summary>
        /// The index of this window
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// True if a render is needed
        /// </summary>
        public bool NeedsRender { get; set; }

        public ClientWindow(int index)
        {
            this.Index = index;
        }

        public virtual void OnClosed()
        {

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
        public Channel Channel { get; set; }

        /// <summary>
        /// The component that belongs to this window
        /// </summary>
        public ChannelWindowComponent Component { get; set; }

        public ChatChannelWindow(int index, Channel channel) : base(index)
        {
            this.Channel = channel;
        }

        public override void OnClosed()
        {
            ClientWindowManager.Instance.CloseWindow(Index);

            // Must be after SetChannelWindowClosed
            base.OnClosed();

            Task.Run(async () =>
            {
                await Component.OnWindowClosed();
            });
        }
    }
}
