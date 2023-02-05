using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_channels")]
[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetVoiceChannel), typeDiscriminator: nameof(PlanetVoiceChannel))]
[JsonDerivedType(typeof(PlanetCategory), typeDiscriminator: nameof(PlanetCategory))]
public abstract class PlanetChannel : Channel, ISharedPlanetChannel
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }
    
    [ForeignKey("ParentId")]
    public PlanetCategory Parent { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Column("planet_id")]
    public long PlanetId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("position")]
    public int Position { get; set; }

    [Column("description")]
    public string Description { get; set; }

    [Column("parent_id")]
    public long? ParentId { get; set; }

    [Column("inherits_perms")]
    public bool InheritsPerms { get; set; }

    public abstract PermChannelType PermType { get; }
}

