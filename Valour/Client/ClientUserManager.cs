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
        /// True if the user is logged in
        /// </summary>
        public static bool IsLoggedIn()
        {
            return !(User == null);
        }

        /// <summary>
        /// Tries to initialize the user client by using a local token
        /// </summary>
        public static async Task<TaskResult> TryInitializeWithLocalToken(HttpClient http, LocalStorageService storage)
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

            return  await InitializeUser(UserSecretToken, http, storage);
        }

        /// <summary>
        /// Initializes the user using a valid user token
        /// </summary>
        public static async Task<TaskResult> InitializeUser(string token, HttpClient http, LocalStorageService storage)
        {
            string response = await http.GetStringAsync($"User/GetUserWithToken?token={token}");

            TaskResult<ClientUser> result = JsonConvert.DeserializeObject<TaskResult<ClientUser>>(response);

            if (result.Success)
            {
                User = result.Data;
                UserSecretToken = token;

                Console.WriteLine($"Initialized user {User.Username}");

                // Store token for future use
                await StoreToken(storage);

                return new TaskResult(true, "Initialized user successfully!");
            }

            return new TaskResult(false, "An error occured retrieving the user.");
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
