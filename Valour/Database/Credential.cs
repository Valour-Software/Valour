using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Valour.Database;

/// <summary>
/// The credential class allows different authentication types to work
/// together in a clean and organized way
/// </summary>
[Table("credentials")]
public class Credential
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    /// <summary>
    /// The ID of this credential
    /// </summary>
    [Column("id")]
    public long Id {get; set; }

    /// <summary>
    /// The ID of the user using this credential
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// The type of credential. This could be password, google, or whatever
    /// way the user is signing in
    /// </summary>
    [Column("credential_type")]
    public string CredentialType { get; set; }

    /// <summary>
    /// This is what identified the user - in the case of normal logins,
    /// this would be the email used to log in.
    /// </summary>
    [Column("identifier")]
    public string Identifier { get; set; }

    /// <summary>
    /// The secret that allows the login - this would be the password
    /// hash for a normal login. This should NOT be able to be reached by the client.
    /// If password hash, should be 32 bytes (256 bits)
    /// </summary>
    [Column("secret")]
    public byte[] Secret { get; set; }

    /// <summary>
    /// The unique salt for the password.
    /// Not to be confused with league of legends players.
    /// This only really applies to a password login.
    /// </summary>
    [Column("salt")]
    public byte[] Salt { get; set; }

    /// <summary>
    /// The PBKDF2 iteration count this secret was hashed with. Stored per
    /// credential so the work factor can be raised over time without
    /// invalidating existing passwords - rows below the current count are
    /// re-hashed on the next successful login. Rows created before this column
    /// existed default to the legacy count.
    /// </summary>
    [Column("iterations")]
    public int Iterations { get; set; }

    /// <summary>
    /// A readable label shown in the account's Connections settings, such as
    /// the email of a linked Google account or the name of a device.
    /// </summary>
    [Column("display_name")]
    public string DisplayName { get; set; }

    /// <summary>
    /// When this sign-in method was added. Null for rows created before this
    /// column existed.
    /// </summary>
    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// When this sign-in method was last used to sign in.
    /// </summary>
    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        // An external account or device key can only belong to one Valour
        // account. Passwords are looked up by email, which is already unique.
        builder.Entity<Credential>()
            .HasIndex(x => new { x.CredentialType, x.Identifier })
            .IsUnique()
            .HasFilter("credential_type <> 'Password'");
    }
}

/// <summary>
/// Contains all the credential type names
/// </summary>
public static class CredentialType
{
    public const string PASSWORD = "Password";

    /// <summary>
    /// A Google account. The identifier is Google's stable account ID ("sub").
    /// </summary>
    public const string GOOGLE = "Google";

    /// <summary>
    /// A Discord account. The identifier is the Discord user ID.
    /// </summary>
    public const string DISCORD = "Discord";

    /// <summary>
    /// A key held in a device's hardware keystore that signs in after a
    /// fingerprint check. The identifier is a random key ID, and the secret is
    /// the public key (SubjectPublicKeyInfo).
    /// </summary>
    public const string DEVICE_KEY = "DeviceKey";

    /// <summary>
    /// Types that can restore access to an account on a new device. A device
    /// key is lost with its device, so it never counts as the last way in.
    /// </summary>
    public static bool CanRecoverAccount(string type) =>
        type is PASSWORD or GOOGLE or DISCORD;
}

