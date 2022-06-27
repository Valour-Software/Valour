using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Database.Items.Planets.Channels;

[Table("PlanetChannels")]
public abstract class PlanetChannel : PlanetItem, ISharedPlanetChannel
{

    [JsonIgnore]
    [ForeignKey("Parent_Id")]
    public PlanetCategoryChannel Parent { get; set; }

    public string Name { get; set; }
    public int Position { get; set; }
    public string Description { get; set; }
    public ulong? Parent_Id { get; set; }
    public bool InheritsPerms { get; set; }

    public override ItemType ItemType => ItemType.PlanetChannel;

    /// <summary>
    /// Returns the parent category of this channel
    /// </summary>
    public async Task<PlanetCategoryChannel> GetParentAsync(ValourDB db)
    {
        Parent ??= await db.PlanetCategoryChannels.FindAsync(Parent_Id);
        return Parent;
    }

    public abstract Task<bool> HasPermissionAsync(PlanetMember member, Permission permission, ValourDB db);

    public static async Task<bool> HasUniquePosition(ValourDB db, PlanetChannel channel) =>
        // Ensure position is not already taken
        !(await db.PlanetChannels.AnyAsync(x => x.Parent_Id == channel.Parent_Id && // Same parent
                                                x.Position == channel.Position && // Same position
                                                x.Id != channel.Id)); // Not self
}

