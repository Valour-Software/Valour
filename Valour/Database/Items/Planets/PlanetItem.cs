using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

namespace Valour.Database.Items.Planets;

/// <summary>
/// This abstract class provides the base for planet-based items
/// </summary>
public abstract class PlanetItem : Item
{
	[JsonIgnore]
	[ForeignKey("Planet_Id")]
	public Planet Planet { get; set; }

	public ulong Planet_Id { get; set; }

    /// <summary>
    /// Returns the planet for this item
    /// </summary>
    public virtual async ValueTask<Planet> GetPlanetAsync(ValourDB db) =>
        await db.Planets.FindAsync(Planet_Id);

    /// <summary>
    /// Returns all of this planet item type within the planet
    /// </summary>
    public virtual async Task<ICollection<T>> FindAllInPlanetAsync<T>(ValourDB db)
        where T : PlanetItem => 
        await db.Set<T>().Where(x => x.Planet_Id == Planet_Id).ToListAsync();

    public override string IdRoute =>
        $"planet/{{planet_id}}/{ItemType}/{{id}}";

    public override string BaseRoute =>
        $"planet/{{planet_id}}/{ItemType}";
}

