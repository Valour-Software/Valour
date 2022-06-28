using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Server.Database.Items.Users;
using Valour.Shared.Items.Authorization;

namespace Valour.Server.Database.Items.Authorization;

public class AuthToken : AuthTokenBase
{
    public static ConcurrentDictionary<string, AuthToken> QuickCache = new ConcurrentDictionary<string, AuthToken>();

    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }

    /// <summary>
    /// Will return the auth object for a valid token, including the user.
    /// This will log the access time in the user object.
    /// A null response means the token was invalid.
    /// </summary>
    public static async Task<AuthToken> TryAuthorize(string token, ValourDB db)
    {
        if (token == null) return null;

        AuthToken authToken = null;

        if (QuickCache.ContainsKey(token))
        {
            authToken = QuickCache[token];
        }
        else
        {
            authToken = await db.AuthTokens.FindAsync(token);

            QuickCache.TryAdd(token, authToken);
        }

        // Spin off a task to do things we don't want to wait on
        var t = Task.Run(async () =>
        {
            using (ValourDB tdb = new ValourDB(ValourDB.DBOptions))
            {
                if (authToken == null)
                {
                    authToken = await tdb.AuthTokens.FindAsync(token);
                }

                if (authToken != null)
                {
                    User user = await tdb.Users.FindAsync(authToken.UserId);
                    user.LastActive = DateTime.UtcNow;

                    await tdb.SaveChangesAsync();
                }
            }
        });


        return authToken;
    }
}

