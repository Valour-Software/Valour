using Valour.Server.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Server.Database.Items.Planets.Channels;

[Table("planet_channels")]
[JsonDerivedType(typeof(PlanetChatChannel), typeDiscriminator: nameof(PlanetChatChannel))]
[JsonDerivedType(typeof(PlanetCategoryChannel), typeDiscriminator: nameof(PlanetCategoryChannel))]
public abstract class PlanetChannel : PlanetItem, ISharedPlanetChannel
{

    [JsonIgnore]
    [ForeignKey("ParentId")]
    public PlanetCategoryChannel Parent { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("position")]
    public int Position { get; set; }

    [Column("description")]
    public string Description { get; set; }

    [Column("parent_id")]
    public ulong? ParentId { get; set; }

    [Column("inherits_perms")]
    public bool InheritsPerms { get; set; }

    /// <summary>
    /// Returns the parent category of this channel
    /// </summary>
    public async Task<PlanetCategoryChannel> GetParentAsync(ValourDB db)
    {
        Parent ??= await db.PlanetCategoryChannels.FindAsync(ParentId);
        return Parent;
    }

    public abstract Task<bool> HasPermissionAsync(PlanetMember member, Permission permission, ValourDB db);

    public static async Task<bool> HasUniquePosition(ValourDB db, PlanetChannel channel) =>
        // Ensure position is not already taken
        !(await db.PlanetChannels.AnyAsync(x => x.ParentId == channel.ParentId && // Same parent
                                                x.Position == channel.Position && // Same position
                                                x.Id != channel.Id)); // Not self
}

