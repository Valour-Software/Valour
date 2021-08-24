using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Valour.Shared;
using Valour.Shared.Users;
using Valour.Shared.Oauth;
using System.Security.Cryptography;
using Blazored.LocalStorage;
using Valour.Shared.Planets;
using System.Runtime.CompilerServices;
using Valour.Client.Planets;

namespace Valour.Client
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// The ClientUserManager provides important User controls for
    /// use on the client. It is static to allow easy access to user data.
    /// </summary>
    public static class ClientUserManager
    {
        /// <summary>
        /// This is the currently logged in user for the client
        /// </summary>
        public static User User { get; set; }

        /// <summary>
        /// This is the token the user is currently using to stay logged in
        /// </summary>
        public static string UserSecretToken { get; set; }

        /// <summary>
        /// Cache for joined planet objects
        /// </summary>
        public static List<ClientPlanet> Planets = new List<ClientPlanet>();

        /// <summary>
        /// Http client
        /// </summary>
        public static HttpClient Http;

        /// <summary>
        /// Random helper
        /// </summary>
        public static Random Random = new Random();

        /// <summary>
        /// True if the user is logged in
        /// </summary>
        public static bool IsLoggedIn()
        {
            return !(User == null);
        }

        /// <summary>
        /// Tries to initialize the user client by using a local token
        /// </summary>
        public static async Task<TaskResult> TryInitializeWithLocalToken(ILocalStorageService storage)
        {
            if (User != null)
            {
                return new TaskResult(false, "There's already a user logged in!");
            }

            await RetrieveToken(storage);

            // Cancel if no token is found
            if (UserSecretToken == null || string.IsNullOrWhiteSpace(UserSecretToken))
            {
                return new TaskResult(false, "Failed to retrieve local token.");
            }

            return await InitializeUser(UserSecretToken, storage);
        }

        /// <summary>
        /// Initializes the user using a valid user token
        /// </summary>
        public static async Task<TaskResult> InitializeUser(string token, ILocalStorageService storage)
        {
            string response = await Http.GetStringAsync($"User/GetUserWithToken?token={token}");

            TaskResult<User> result = JsonConvert.DeserializeObject<TaskResult<User>>(response);

            if (result.Success)
            {
                User = result.Data;
                UserSecretToken = token;

                Http.DefaultRequestHeaders.Add("authorization", UserSecretToken);

                Console.WriteLine($"Initialized user {User.Username}");

                // Store token for future use
                await StoreToken(storage);

                // Refresh user planet membership
                await RefreshPlanetsAsync();

                return new TaskResult(true, "Initialized user successfully!");
            }

            return new TaskResult(false, "An error occured retrieving the user.");
        }

        /// <summary>
        /// Retrieves and updates the current planets that the user is a member of
        /// </summary>
        public static async Task RefreshPlanetsAsync()
        {
            var response = await Http.GetAsync($"api/user/{User.Id.ToString()}/planets");

            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Fatal error retrieving user planet membership");
                Console.WriteLine(message);
                return;
            }

            List<ClientPlanet> planets = JsonConvert.DeserializeObject<List<ClientPlanet>>(message);

            if (planets == null)
            {
                Console.WriteLine("Fatal error deserializing member list");
                Console.WriteLine(message);
                return;
            }
            
            await Parallel.ForEachAsync(planets, async (planet, token) =>
            {
                // Load planet into cache
                await ClientPlanetManager.Current.AddPlanetAsync(planet);
                Planets.Add(planet);
            });
        }

        /// <summary>
        /// Stores the current user token
        /// </summary>
        public static async Task StoreToken(ILocalStorageService storage)
        {
            LocalToken tokenObj = new LocalToken()
            {
                Token = UserSecretToken
            };

            await storage.SetItemAsync("token", tokenObj);

            Console.WriteLine("Stored user token in local storage.");
        }

        /// <summary>
        /// Attempts to retrieve a prior user token
        /// </summary>
        public static async Task RetrieveToken(ILocalStorageService storage)
        {
            LocalToken tokenObj = await storage.GetItemAsync<LocalToken>("token");

            if (tokenObj != null)
            {
                UserSecretToken = tokenObj.Token;
            }
        }
    }
}