using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Items.Planets;

namespace Valour.Database.Items.Planets;

/// <summary>
/// This abstract class provides the base for planet-based items
/// </summary>
public abstract class PlanetItem<T> : Item, ISharedPlanetItem, INodeSpecific where T : PlanetItem<T>
{
	[JsonIgnore]
	[ForeignKey("Planet_Id")]
	public Planet Planet { get; set; }

	public ulong Planet_Id { get; set; }

    /// <summary>
    /// Returns the item with the given id
    /// </summary>
    public virtual async Task<T> FindAsync(ulong id, ValourDB db) =>
        await db.FindAsync<T>(id);

    /// <summary>
    /// Returns all of this planet item type within the planet
    /// </summary>
    public virtual async Task<ICollection<T>> FindAllAsync(ValourDB db) => 
        await db.Set<T>().Where(x => x.Planet_Id == Planet_Id).ToListAsync();

    /// <summary>
    /// Deletes this item from the database
    /// </summary>
    public virtual async Task DeleteAsync(ValourDB db)
    {
        db.Remove((T)this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemDelete((T)this);
    }
     
    /// <summary>
    /// Updates this item in the database
    /// </summary>
    public virtual async Task UpdateAsync(ValourDB db)
    {
        db.Update((T)this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemChange((T)this);
    }

    /// <summary>
    /// Creates this item in the database
    /// </summary>
    public virtual async Task CreateAsync(ValourDB db)
    {
        await db.AddAsync((T)this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemChange((T)this);
    }

    /// <summary>
    /// Success if a member has permission to get this
    /// item via the API. By default this is true if the member exists.
    /// </summary>
    public virtual async Task<TaskResult> CanGetAsync(PlanetMember member, ValourDB db)
    {
        if (member is null)
            return await Task.FromResult(
                new TaskResult(false, "Member not found.")
            );

        return await Task.FromResult(
            new TaskResult(true, "Success")
        );
    }

    /// <summary>
    /// Success if a member has permission to get all of this item type within the planet
    /// via the API. By default this is true if the member exists.
    /// </summary>
    /// <param name="member"></param>
    /// <param name="db"></param>
    /// <returns></returns>
    public virtual async Task<TaskResult> CanGetAllAsync(PlanetMember member, ValourDB db)
    {
        if (member is null)
            return await Task.FromResult(
                new TaskResult(false, "Member not found.")
            );

        return await Task.FromResult(
            new TaskResult(true, "Success")
        );
    }

    /// <summary>
    /// Success if a member has permission to delete this
    /// item via the API
    /// </summary>
    public abstract Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db);

    /// <summary>
    /// Success if a member has permission to update this
    /// item via the API
    /// </summary>
    public abstract Task<TaskResult> CanUpdateAsync(PlanetMember member, ValourDB db);

    /// <summary>
    /// Success if a member has permission to create this
    /// item via the API
    /// </summary>
    public abstract Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db);

    /// <summary>
    /// Returns true if this is a valid item (can be POSTed)
    /// </summary>
    public abstract Task<TaskResult> ValidateItemAsync(ulong planet_id, ValourDB db);
}

