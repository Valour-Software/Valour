using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Items;

namespace Valour.Database.Items;

/// <summary>
/// Common interface for items that support API actions
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IPlanetItemAPI<T> where T : class, IPlanetItemAPI<T>
{
    [Key]
    [JsonInclude]
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [NotMapped]
    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public abstract ItemType ItemType { get; }

    [JsonInclude]
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    /// <summary>
    /// Returns the item with the given id
    /// </summary>
    async Task<T> FindAsync(ulong id, ValourDB db)
    {
        return await db.FindAsync<T>(id);
    }

    ICollection<T> FindAllAsync(ValourDB db)
    {
        return db.Set<T>().Where(x => x.Planet_Id == Planet_Id).ToList();
    }

    /// <summary>
    /// Deletes this item from the database
    /// </summary>
    async Task DeleteAsync(ValourDB db)
    {
        db.Remove((T)this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemDelete(this);
    }

    /// <summary>
    /// Updates this item in the database
    /// </summary>
    async Task UpdateAsync(ValourDB db)
    {
        db.Update((T)this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemChange(this);
    }

    /// <summary>
    /// Creates this item in the database
    /// </summary>
    async Task CreateAsync(ValourDB db)
    {
        await db.AddAsync((T)this);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemChange(this);
    }

    /// <summary>
    /// Success if a member has permission to get this
    /// item via the API
    /// </summary>
    Task<TaskResult> CanGetAsync(PlanetMember member, ValourDB db);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="member"></param>
    /// <param name="db"></param>
    /// <returns></returns>
    Task<TaskResult> CanGetAllAsync(PlanetMember member, ValourDB db);

    /// <summary>
    /// Success if a member has permission to delete this
    /// item via the API
    /// </summary>
    Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db);

    /// <summary>
    /// Success if a member has permission to update this
    /// item via the API
    /// </summary>
    Task<TaskResult> CanUpdateAsync(PlanetMember member, ValourDB db);

    /// <summary>
    /// Success if a member has permission to create this
    /// item via the API
    /// </summary>
    Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db);

    /// <summary>
    /// Returns true if this is a valid item (can be POSTed)
    /// </summary>
    Task<TaskResult> ValidateItemAsync(ulong planet_id, ValourDB db);
}

