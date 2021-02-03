using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Client
{
    public class SignalRManager
    {
        public static SignalRManager Current;

        private readonly NavigationManager navManager;

        public HubConnection hubConnection;

        public SignalRManager(NavigationManager navmanager)
        {
            navManager = navmanager;
            Current = this;
        }

        public async Task ConnectPlanetHub()
        {
            Console.WriteLine("Connecting to Planet Hub");

            Console.WriteLine(navManager.BaseUri);

            // Get url for
            string conUrl = navManager.BaseUri.TrimEnd('/') + "/planethub";
            //string conUrl = "https://valour.gg/planethub";

            hubConnection = new HubConnectionBuilder()
                .WithUrl(conUrl)
                .WithAutomaticReconnect()
                .Build();

            //hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(30);
            hubConnection.Closed += OnClosed;
            hubConnection.Reconnected += OnReconnect;

            await hubConnection.StartAsync();
        }

        /// <summary>
        /// Forces SignalR to refresh the underlying connection
        /// </summary>
        public async Task ForceRefresh()
        {
            Console.WriteLine("Forcing SignalR refresh.");

            if (hubConnection.State == HubConnectionState.Disconnected)
            {
                Console.WriteLine("Disconnected.");
                await Reconnect();
            }
        }

        /// <summary>
        /// Reconnects the SignalR connection
        /// </summary>
        public async Task Reconnect()
        {
            // Reconnect
            await hubConnection.StartAsync();
            Console.WriteLine("Reconnecting to Planet Hub");

            await OnReconnect("");
        }

        /// <summary>
        /// Attempt to recover the connection if it is lost
        /// </summary>
        public async Task OnClosed(Exception e)
        {
            // Ensure disconnect was not on purpose
            if (e != null)
            {
                Console.WriteLine("## A Breaking SignalR Error Has Occured");
                Console.WriteLine("Exception: " + e.Message);
                Console.WriteLine("Stacktrace: " + e.StackTrace);

                await Reconnect();
            }
            else
            {
                Console.WriteLine("SignalR has closed without error.");

                await Reconnect();
            }
        }

        public async Task OnReconnect(string data)
        {
            Console.WriteLine("SignalR has reconnected: ");
            Console.WriteLine(data);

            await Planets.ClientPlanetManager.Current.HandleReconnect();
        }
    }
}
