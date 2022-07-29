using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Valour.Server.Database.Items.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;

namespace Valour.Server.Database.Items.Authorization;

[Table("auth_tokens")]
public class AuthToken : ISharedAuthToken
{
    [NotMapped]
    [JsonIgnore]
    public static ConcurrentDictionary<string, AuthToken> QuickCache = new ConcurrentDictionary<string, AuthToken>();

    [NotMapped]
    [JsonIgnore]
    public static ConcurrentDictionary<long, DateTime?> UserTimeCache = new ConcurrentDictionary<long, DateTime?>();

    [Key]
    [Column("id")]
    public string Id { get; set; }

    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }

    /// <summary>
    /// The ID of the app that has been issued this token
    /// </summary>
    [Column("app_id")]
    public string AppId { get; set; }

    /// <summary>
    /// The user that this token is valid for
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// The scope of the permissions this token is valid for
    /// </summary>
    [Column("scope")]
    public long Scope { get; set; }

    /// <summary>
    /// The time that this token was issued
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time that this token will expire
    /// </summary>
    [Column("time_expires")]
    public DateTime TimeExpires { get; set; }

    /// <summary>
    /// Returns whether the auth token has the given scope
    /// </summary>
    public bool HasScope(Permission permission) =>
        ISharedAuthToken.HasScope(permission, this);

    /// <summary>
    /// Will return the auth object for a valid token, including the user.
    /// This will log the access time in the user object.
    /// A null response means the token was invalid.
    /// </summary>
    public static async Task<AuthToken> TryAuthorize(string token, ValourDB db)
    {
        if (token == null) return null;

        AuthToken authToken;

        if (QuickCache.ContainsKey(token))
        {
            authToken = QuickCache[token];
        }
        else
        {
            authToken = await db.AuthTokens.FindAsync(token);

            QuickCache.TryAdd(token, authToken);
        }

        // Now using a time cache to significantly improve performance here.
        // This is a really hot path (literally almost every API path uses this)
        // So any further optimization would be great
        if (authToken != null)
        {
            UserTimeCache.TryGetValue(authToken.UserId, out DateTime? lastActiveCached);

            DateTime lastActive = lastActiveCached ?? DateTime.MinValue;

            // Only bother updating if it's been at least 30 seconds
            // since the last activity update
            if (lastActive.AddSeconds(30) < DateTime.UtcNow)
            {
                User user = await db.Users.FindAsync(authToken.UserId);
                user.TimeLastActive = DateTime.UtcNow;
                UserTimeCache[user.Id] = user.TimeLastActive;

                await db.SaveChangesAsync();

                // Notify of user activity change
                PlanetHub.NotifyUserChange(user, db);
            }
        }

        return authToken;
    }
}

