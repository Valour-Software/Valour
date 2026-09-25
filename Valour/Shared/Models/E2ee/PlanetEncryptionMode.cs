namespace Valour.Shared.Models;

/// <summary>
/// Who receives a planet's channel keys. Every planet's messages are end-to-end
/// encrypted; the mode decides who members share keys with.
/// </summary>
public enum PlanetEncryptionMode
{
    /// <summary>
    /// Members share channel keys with anyone the planet's permissions allow
    /// to view a channel, so anyone who can join can read. Every account
    /// holding keys is visible to members.
    /// </summary>
    Open = 0,

    /// <summary>
    /// Members share keys only with users admitted by a planet admin's device
    /// or a signed invite. The server cannot admit anyone, including itself.
    /// A planet can become invite-only but cannot become open again.
    /// </summary>
    InviteOnly = 1
}
