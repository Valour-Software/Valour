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

    /// <summary>
    /// Deletes this item from the database
    /// </summary>
    public virtual async Task DeleteAsync(ValourDB db)
    {
        db.Remove(this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemDelete(this);
    }
     
    /// <summary>
    /// Updates this item in the database
    /// </summary>
    public virtual async Task UpdateAsync(ValourDB db)
    {
        db.Update(this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemChange(this);
    }

    /// <summary>
    /// Creates this item in the database
    /// </summary>
    public virtual async Task CreateAsync(ValourDB db)
    {
        await db.AddAsync(this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemChange(this);
    }

    /// <summary>
    /// Success if a member has permission to get this
    /// item via the API. By default this is true if the member exists.
    /// </summary>
    public virtual async Task<TaskResult> CanGetAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        if (member is null)
            return await Task.FromResult(
                new TaskResult(false, "Member not found.")
            );

        return await Task.FromResult(
            TaskResult.SuccessResult
        );
    }

    /// <summary>
    /// Success if a member has permission to get all of this item type within the planet
    /// via the API. By default this is true if the member exists.
    /// </summary>
    public virtual async Task<TaskResult> CanGetAllAsync(AuthToken token,  PlanetMember member, ValourDB db)
    {
        if (member is null)
            return await Task.FromResult(
                new TaskResult(false, "Member not found.")
            );

        return await Task.FromResult(
            TaskResult.SuccessResult
        );
    }

    /// <summary>
    /// Success if a member has permission to delete this
    /// item via the API
    /// </summary>
    public abstract Task<TaskResult> CanDeleteAsync(AuthToken token, PlanetMember member, ValourDB db);

    /// <summary>
    /// Success if a member has permission to update this
    /// item via the API. Old is the old version of the item.
    /// </summary>
    public abstract Task<TaskResult> CanUpdateAsync(AuthToken token, PlanetMember member, PlanetItem old, ValourDB db);

    /// <summary>
    /// Success if a member has permission to create this
    /// item via the API
    /// </summary>
    public abstract Task<TaskResult> CanCreateAsync(AuthToken token, PlanetMember member, ValourDB db);
}

