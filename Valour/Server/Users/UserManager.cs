using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared.Users;

namespace Valour.Server.Users
{
    public class UserManager
    {
        /// <summary>
        /// Validates and returns a User using credentials (async)
        /// </summary>
        public async Task<User> ValidateAsync(string credential_type, string identifier, string secret)
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
                    return null;
                }

                return await context.Users.FindAsync(credential.User_Id);
            }
        }

        /// <summary>
        /// Validates and returns a User using credentials
        /// </summary>
        public User Validate(string credential_type, string identifier, string secret)
        {
            return ValidateAsync(credential_type, identifier, secret).Result;
        }
    }
}
