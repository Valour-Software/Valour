using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;

namespace Valour.Database.Items;

/// <summary>
/// Common interface for items that support API actions
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IPlanetItemAPI<T>
{
    /// <summary>
    /// Returns the item with the given id
    /// </summary>
    Task<T> FindAsync(ulong id, ValourDB db);

    /// <summary>
    /// Deletes this item from the database
    /// </summary>
    Task DeleteAsync(ValourDB db);

    /// <summary>
    /// Updates this item in the database
    /// </summary>
    Task UpdateAsync(T updated, ValourDB db);

    /// <summary>
    /// Creates this item in the database
    /// </summary>
    Task CreateAsync(ValourDB db);

    /// <summary>
    /// Success if a member has permission to get this
    /// item via the API
    /// </summary>
    Task<TaskResult> CanGetAsync(PlanetMember member, ValourDB db);

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

