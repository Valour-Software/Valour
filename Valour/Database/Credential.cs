using System.ComponentModel.DataAnnotations.Schema;

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
}

/// <summary>
/// Contains all the credential type names
/// </summary>
public static class CredentialType
{
    public const string PASSWORD = "Password";
}

