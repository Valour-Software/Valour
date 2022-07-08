using Valour.Shared;
using Valour.Shared.Items.Authorization;
using Blazored.LocalStorage;
using Valour.Api.Client;
using Microsoft.AspNetCore.Components;
using Valour.Api.Items.Users;

namespace Valour.Client.Blazor
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
        /// Tries to initialize the user client by using a local token
        /// </summary>
        public static async Task<TaskResult<User>> TryInitializeWithLocalToken(ILocalStorageService storage, NavigationManager nav)
        {
            if (ValourClient.IsLoggedIn)
                return new TaskResult<User>(false, "There's already a user logged in!");

            var token = await RetrieveToken(storage);

            // Cancel if no token is found
            if (token == null || string.IsNullOrWhiteSpace(token.Token))
            {
                return new TaskResult<User>(false, "Failed to retrieve local token.");
            }

            return await InitializeUser(token.Token, storage, nav);
        }

        /// <summary>
        /// Initializes the user using a valid user token
        /// </summary>
        public static async Task<TaskResult<User>> InitializeUser(string token, ILocalStorageService storage, NavigationManager nav)
        {
            // Store token for future use
            var user = await ValourClient.InitializeUser(token);
            await StoreToken(storage);
            return user;
        }

        /// <summary>
        /// Stores the current user token
        /// </summary>
        public static async Task StoreToken(ILocalStorageService storage)
        {
            LocalToken tokenObj = new()
            {
                Token = ValourClient.Token
            };

            await storage.SetItemAsync("token", tokenObj);

            Console.WriteLine("Stored user token in local storage.");
        }

        /// <summary>
        /// Attempts to retrieve a prior user token
        /// </summary>
        public static async Task<LocalToken> RetrieveToken(ILocalStorageService storage) =>
            await storage.GetItemAsync<LocalToken>("token");
        
    }
}