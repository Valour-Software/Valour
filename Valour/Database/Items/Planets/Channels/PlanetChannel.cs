using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Database.Items.Planets.Channels;

[Table("PlanetChannels")]
public abstract class PlanetChannel : Channel, ISharedPlanetChannel
{
    [JsonIgnore]
    [ForeignKey("Planet_Id")]
    public Planet Planet { get; set; }

    [JsonIgnore]
    [ForeignKey("Parent_Id")]
    public PlanetCategoryChannel Parent { get; set; }

    [JsonInclude]
    [JsonPropertyName("Parent_Id")]
    public ulong? Parent_Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    public override ItemType ItemType => ItemType.PlanetChannel;

    public async Task<Planet> GetPlanetAsync(ValourDB db)
    {
        Planet ??= await Planet.FindAsync(Planet_Id, db);
        return Planet;
    }

    /// <summary>
    /// Returns the parent category of this channel
    /// </summary>
    public async Task<PlanetCategoryChannel> GetParentAsync(ValourDB db)
    {
        Parent ??= await db.PlanetCategories.FindAsync(Parent_Id);
        return Parent;
    }

    public abstract void NotifyClientsChange();
}

