using Newtonsoft.Json;
using System;
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
using AutoMapper;

namespace Valour.Client
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2020 Vooper Media LLC
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
        public static ClientUser User { get; set; }

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
        /// True if the user is logged in
        /// </summary>
        public static bool IsLoggedIn()
        {
            return !(User == null);
        }

        /// <summary>
        /// Tries to initialize the user client by using a local token
        /// </summary>
        public static async Task<TaskResult> TryInitializeWithLocalToken(LocalStorageService storage, IMapper mapper)
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

            return  await InitializeUser(UserSecretToken, storage, mapper);
        }

        /// <summary>
        /// Initializes the user using a valid user token
        /// </summary>
        public static async Task<TaskResult> InitializeUser(string token, LocalStorageService storage, IMapper mapper)
        {
            string response = await Http.GetStringAsync($"User/GetUserWithToken?token={token}");

            TaskResult<ClientUser> result = JsonConvert.DeserializeObject<TaskResult<ClientUser>>(response);

            if (result.Success)
            {
                User = result.Data;
                UserSecretToken = token;

                Console.WriteLine($"Initialized user {User.Username}");

                // Store token for future use
                await StoreToken(storage);

                // Refresh user planet membership
                await RefreshPlanetsAsync(mapper);

                return new TaskResult(true, "Initialized user successfully!");
            }

            return new TaskResult(false, "An error occured retrieving the user.");
        }

        /// <summary>
        /// Retrieves and updates the current planets that the user is a member of
        /// </summary>
        public static async Task RefreshPlanetsAsync(IMapper mapper)
        {
            string json = await Http.GetStringAsync($"User/GetPlanetMembership?id={User.Id}&token={UserSecretToken}");

            TaskResult<List<Planet>> response = JsonConvert.DeserializeObject<TaskResult<List<Planet>>>(json);

            Console.WriteLine(response.Message);

            if (response.Success)
            {
                foreach (Planet planet in response.Data)
                {
                    Planets.Add(ClientPlanet.FromBase(planet, mapper));
                }
            }
        }

        public static void RefreshPlanets(IMapper mapper)
        {
            RefreshPlanetsAsync(mapper).RunSynchronously();
        }

        /// <summary>
        /// Stores the current user token
        /// </summary>
        public static async Task StoreToken(LocalStorageService storage)
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
        public static async Task RetrieveToken(LocalStorageService storage)
        {
            LocalToken tokenObj = await storage.GetItemAsync<LocalToken>("token");

            if (tokenObj != null)
            {
                UserSecretToken = tokenObj.Token;
            }
        }
    }
}
