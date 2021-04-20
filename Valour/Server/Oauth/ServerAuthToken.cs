using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Users;
using Valour.Shared.Oauth;

namespace Valour.Server.Oauth
{
    public class ServerAuthToken : AuthToken
    {
        [ForeignKey("User_Id")]
        public virtual ServerUser User { get; set; }

        /// <summary>
        /// Will return the auth object for a valid token, including the user.
        /// This will log the access time in the user object.
        /// A null response means the token was invalid.
        /// </summary>
        public static async Task<ServerAuthToken> TryAuthorize(string token, ValourDB db)
        {
            bool createdb = false;
            if (db == null)
            {
                db = new ValourDB(ValourDB.DBOptions);
                createdb = true;
            }

            ServerAuthToken auth = await db.AuthTokens.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == token);
            if (auth == null) return null;
            if (auth.User == null) return null;
            auth.User.Last_Active = DateTime.UtcNow;
            await db.SaveChangesAsync();

            if (createdb)
            {
                await db.DisposeAsync();
            }

            return auth;
        }
    }
}
