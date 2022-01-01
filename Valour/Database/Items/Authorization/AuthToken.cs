using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;

namespace Valour.Database.Items.Authorization;

public class AuthToken : ISharedAuthToken
{
    public static ConcurrentDictionary<string, AuthToken> QuickCache = new ConcurrentDictionary<string, AuthToken>();

    [ForeignKey("User_Id")]
    [JsonIgnore]
    public virtual User User { get; set; }

    /// <summary>
    /// The ID of the authentification key is also the secret key. Really no need for another random gen.
    /// </summary>
    [Key]
    [JsonPropertyName("Id")]
    public string Id { get; set; }

    /// <summary>
    /// The ID of the app that has been issued this token
    /// </summary>
    [JsonPropertyName("App_Id")]
    public string App_Id { get; set; }

    /// <summary>
    /// The user that this token is valid for
    /// </summary>
    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    /// <summary>
    /// The scope of the permissions this token is valid for
    /// </summary>
    [JsonPropertyName("Scope")]
    public ulong Scope { get; set; }

    /// <summary>
    /// The time that this token was issued
    /// </summary>
    [JsonPropertyName("Time")]
    public DateTime Time { get; set; }

    /// <summary>
    /// The time that this token will expire
    /// </summary>
    [JsonPropertyName("Expires")]
    public DateTime Expires { get; set; }

    [NotMapped]
    [JsonPropertyName("ItemType")]
    public ItemType ItemType => ItemType.AuthToken;

    /// <summary>
    /// Helper method for scope checking
    /// </summary>
    public bool HasScope(UserPermission permission) =>
        Permission.HasPermission(Scope, permission);

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
                    User user = await tdb.Users.FindAsync(authToken.User_Id);
                    user.Last_Active = DateTime.UtcNow;

                    await tdb.SaveChangesAsync();
                }
            }
        });


        return authToken;
    }
}

