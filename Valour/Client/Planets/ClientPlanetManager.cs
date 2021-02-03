using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Channels;
using Valour.Client.Messages;
using Valour.Client.Shared;

namespace Valour.Client.Planets
{
    public class ClientPlanetManager
    {
        public static ClientPlanetManager Current;

        public ClientPlanetManager()
        {
            Current = this;
        }

        /// <summary>
        /// The currently focused planet
        /// </summary>
        private ClientPlanet CurrentPlanet { get; set; }

        private Dictionary<ulong, ClientPlanet> OpenPlanets = new Dictionary<ulong, ClientPlanet>();
        private Dictionary<ulong, ClientPlanetChatChannel> OpenPlanetChatChannels = new Dictionary<ulong, ClientPlanetChatChannel>();
        private List<ChannelWindowComponent> OpenPlanetChatWindows = new List<ChannelWindowComponent>();

        public event Func<Task> OnPlanetChange;

        public event Func<Task> OnChannelsUpdate;

        public event Func<Task> OnChannelWindowUpdate;

        public event Func<Task> OnCategoriesUpdate;

        private readonly SignalRManager signalRManager;

        public ClientPlanetManager(SignalRManager signalrmanager)
        {
            this.signalRManager = signalrmanager;

            signalRManager.hubConnection.On<string>("Relay", OnMessageRecieve);
        }

        public List<ClientPlanet> GetOpenPlanets()
        {
            return OpenPlanets.Values.ToList();
        }

        /// <summary>
        /// Attempts to rejoin all lost connections during a reconnect
        /// </summary>
        public async Task HandleReconnect()
        {
            foreach (ClientPlanet planet in OpenPlanets.Values)
            {
                await signalRManager.hubConnection.SendAsync("JoinPlanet", planet.Id, ClientUserManager.UserSecretToken);
                Console.WriteLine($"Rejoined SignalR group for planet {planet.Id}");
            }

            foreach (ClientPlanetChatChannel channel in OpenPlanetChatChannels.Values)
            {
                await signalRManager.hubConnection.SendAsync("JoinChannel", channel.Id, ClientUserManager.UserSecretToken);
                Console.WriteLine($"Rejoined SignalR group for channel {channel.Id}");
            }

            foreach (ChannelWindowComponent window in OpenPlanetChatWindows)
            {
                await window.SetupNewChannelAsync();
            }
        }

        public async Task OpenPlanet(ClientPlanet planet)
        {
            if (OpenPlanets.ContainsKey(planet.Id))
            {
                // Already opened
                return;
            }

            // Joins planet via SignalR
            await signalRManager.hubConnection.SendAsync("JoinPlanet", planet.Id, ClientUserManager.UserSecretToken);

            // Add to open planet list
            OpenPlanets.Add(planet.Id, planet);

            Console.WriteLine($"Joined SignalR group for planet {planet.Id}");
        }

        public async Task ClosePlanet(ClientPlanet planet)
        {
            // Joins planet via SignalR
            await signalRManager.hubConnection.SendAsync("LeavePlanet", planet.Id);

            // Remove from list
            OpenPlanets.Remove(planet.Id);

            Console.WriteLine($"Left SignalR group for planet {planet.Id}");
        }

        public async Task OpenPlanetChatChannel(ChannelWindowComponent window)
        {
            ClientPlanetChatChannel channel = window.Channel;

            if (!OpenPlanetChatChannels.ContainsKey(channel.Id))
            {
                ClientPlanet planet = await channel.GetPlanetAsync();

                // Ensure planet is opened
                await OpenPlanet(planet);

                // Joins channel via SignalR
                await signalRManager.hubConnection.SendAsync("JoinChannel", channel.Id, ClientUserManager.UserSecretToken);

                // Add to open channel list
                OpenPlanetChatChannels.Add(channel.Id, window.Channel);

                Console.WriteLine($"Joined SignalR group for channel {channel.Id}");
            }

            OpenPlanetChatWindows.Add(window);
        }

        public async Task ClosePlanetChatChannel(ChannelWindowComponent window)
        {
            ClientPlanetChatChannel channel = window.Channel;

            OpenPlanetChatWindows.Remove(window);

            // If there are no longer any windows open for the channel, leave
            if (!OpenPlanetChatWindows.Any(x => x.Channel.Id == channel.Id))
            {
                // Leaves channel via signalr
                await signalRManager.hubConnection.SendAsync("LeaveChannel", channel.Id);

                // Remove from list
                OpenPlanetChatChannels.Remove(channel.Id);

                Console.WriteLine($"Left SignalR group for channel {channel.Id}");
            } 
        }

        public async Task RefreshOpenedChannels() {
            await OnChannelWindowUpdate.Invoke();
        }

        public async Task RefreshChannels() {
            await OnChannelsUpdate.Invoke();
        }

        public async Task RefreshCategories() {
            await OnCategoriesUpdate.Invoke();
        }

        public async Task SetCurrentPlanet(ClientPlanet planet)
        {
            if (planet == null || (CurrentPlanet != null && CurrentPlanet.Id == planet.Id)) return;

            CurrentPlanet = planet;

            Console.WriteLine($"Set current planet to {planet.Id}");

            if (OnPlanetChange != null)
            {
                Console.WriteLine($"Invoking planet change event");
                await OnPlanetChange?.Invoke();
            }
        }

        public ClientPlanet GetCurrent()
        {
            return CurrentPlanet;
        }

        public override string ToString()
        {
            return $"Planet: {CurrentPlanet.Id}";
        }

        public async Task OnMessageRecieve(string json)
        {
            ClientPlanetMessage message = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientPlanetMessage>(json);

            Console.WriteLine("RECIEVE: ");
            Console.WriteLine(json);

            Console.WriteLine($"Recieved message {message.Message_Index} from channel {message.Channel_Id}.");

            if (!OpenPlanetChatChannels.ContainsKey(message.Channel_Id))
            {
                Console.WriteLine("Error: Recieved a message for a closed channel.");
            }

            foreach (ChannelWindowComponent window in OpenPlanetChatWindows.Where(x => x.Channel.Id == message.Channel_Id))
            {
                await window.OnRecieveMessage(message);
            }
        }
    }
}
