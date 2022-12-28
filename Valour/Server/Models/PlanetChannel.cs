using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;

namespace Valour.Server.Models;

[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetVoiceChannel), typeDiscriminator: nameof(PlanetVoiceChannel))]
[JsonDerivedType(typeof(PlanetCategory), typeDiscriminator: nameof(PlanetCategory))]
public abstract class PlanetChannel : Channel, ISharedPlanetChannel
{
    public long PlanetId { get; set; }
    
    public string Name { get; set; }
    
    public int Position { get; set; }
    
    public string Description { get; set; }
    
    public long? ParentId { get; set; }
    
    public bool InheritsPerms { get; set; }

    public abstract PermissionsTargetType PermissionsTargetType { get; }
}

