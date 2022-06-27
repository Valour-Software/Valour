using Microsoft.EntityFrameworkCore;
using Valour.Database;
using Valour.Database.Items.Users;
using Valour.Shared;

namespace Valour.Database.Users.Identity;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public static class UserManager
{

    /// <summary>
    /// Validates and returns a User using credentials (async)
    /// </summary>
    public static async Task<TaskResult<User>> ValidateAsync(string credential_type, string identifier, string secret, ValourDB db)
    {
        // Find the credential that matches the identifier and type
        Credential credential = await db.Credentials.FirstOrDefaultAsync(
            x => string.Equals(credential_type.ToUpper(), x.CredentialType.ToUpper()) &&
                    string.Equals(identifier.ToUpper(), x.Identifier.ToUpper()));

        if (credential == null || string.IsNullOrWhiteSpace(secret))
        {
            return new TaskResult<User>(false, "The credentials were incorrect.", null);
        }

        // Use salt to validate secret hash
        byte[] hash = PasswordManager.GetHashForPassword(secret, credential.Salt);

        // Spike needs to remember how reference types work 
        if (!hash.SequenceEqual(credential.Secret))
        {
            return new TaskResult<User>(false, "The credentials were incorrect.", null);
        }

        User user = await db.Users.FindAsync(credential.User_Id);

        if (user.Disabled)
        {
            return new TaskResult<User>(false, "This account has been disabled", null);
        }

        return new TaskResult<User>(true, "Succeeded", user);
        
    }
}
