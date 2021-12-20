
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

namespace Valour.Database.Items.Planets;
public class Invite : Item, ISharedInvite
{
    [ForeignKey("Planet_Id")]
    public virtual Planet Planet { get; set; }

    /// <summary>
    /// the invite code
    /// </summary>
    [JsonPropertyName("Code")]
    public string Code { get; set; }

    /// <summary>
    /// The planet the invite is for
    /// </summary>
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    [JsonPropertyName("Issuer_Id")]
    public ulong Issuer_Id { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    [JsonPropertyName("Time")]
    public DateTime Time { get; set; }

    /// <summary>
    /// The length of the invite before its invaild
    /// </summary>
    [JsonPropertyName("Hours")]
    public int? Hours { get; set; }

    public bool IsPermanent() => ((ISharedInvite)this).IsPermanent();

    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.Invite;
}
