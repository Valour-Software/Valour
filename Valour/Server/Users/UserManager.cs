using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Users;

namespace Valour.Server.Users
{
    public class UserManager
    {

        /// <summary>
        /// The result from a user validation attempt
        /// </summary>
        public class ValidateResult
        {
            /// <summary>
            /// The result for the validation
            /// </summary>
            public TaskResult Result { get; set; }

            /// <summary>
            /// The user retrieved (if any)
            /// </summary>
            public User User { get; set; }

            public ValidateResult(TaskResult result, User user)
            {
                this.Result = result;
                this.User = user;
            }
        }

        /// <summary>
        /// Validates and returns a User using credentials (async)
        /// </summary>
        public async Task<ValidateResult> ValidateAsync(string credential_type, string identifier, string secret)
        {
            using (ValourDB context  = new ValourDB(ValourDB.DBOptions))
            {
                // Find the credential that matches the identifier and type
                Credential credential = await context.Credentials.FirstOrDefaultAsync(
                    x => string.Equals(credential_type, x.Credential_Type, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(identifier, x.Identifier, StringComparison.OrdinalIgnoreCase));

                // Use salt to validate secret hash
                byte[] hash = PasswordManager.GetHashForPassword(secret, credential.Salt);

                if (hash != credential.Secret)
                {
                    return new ValidateResult(new TaskResult(false, "Secret validation failed."), null);
                }

                User user = await context.Users.FindAsync(credential.User_Id);

                return new ValidateResult(new TaskResult(true, "Succeeded"), user);
            }
        }

        /// <summary>
        /// Validates and returns a User using credentials
        /// </summary>
        public ValidateResult Validate(string credential_type, string identifier, string secret)
        {
            return ValidateAsync(credential_type, identifier, secret).Result;
        }

        /// <summary>
        /// Logs the user in 
        /// </summary>
        public async Task LogInUser(HttpContext http, User user, bool isPersistant = false)
        {
            ClaimsIdentity identity = new ClaimsIdentity();
        }

        /// <summary>
        /// Returns the role claims for a given user
        /// </summary>
        private IEnumerable<Claim> GetUserRoleClaims(User user)
        {
            List<Claim> claims = new List<Claim>();
            IEnumerable<int> roleIds 
        }
    }
}
