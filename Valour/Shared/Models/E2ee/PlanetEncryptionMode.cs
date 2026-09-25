namespace Valour.Shared.Models;

/// <summary>
/// Who receives a planet's channel keys. Every planet's messages are end-to-end
/// encrypted; the mode decides who members share keys with. A public planet
/// is always open. A private planet is invite-only once its owner's device has
/// signed its membership log.
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
    /// The planet becomes open again only through an entry the owner signs in
    /// the membership log.
    /// </summary>
    InviteOnly = 1
}
