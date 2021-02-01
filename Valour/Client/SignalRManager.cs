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

            hubConnection = new HubConnectionBuilder()
                .WithUrl(conUrl)
                .WithAutomaticReconnect()
                .Build();

            hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(30);
            hubConnection.Closed += OnClosed;

            await hubConnection.StartAsync();
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

                // Reconnect
                await hubConnection.StartAsync();

                Console.WriteLine("Reconnecting to Planet Hub");
            }
            else
            {
                Console.WriteLine("SignalR has closed without error.");

                await hubConnection.StartAsync();

                Console.WriteLine("Reconnecting to Planet Hub");
            }
        }
    }
}
