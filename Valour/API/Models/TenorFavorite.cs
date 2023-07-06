using Valour.Api.Client;
using Valour.Api.Models;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Api.Models;

public class TenorFavorite : LiveModel, ISharedTenorFavorite
{
    #region IPlanetModel implementation

    public override string BaseRoute =>
            $"api/tenorfavorites";

    #endregion
    /// <summary>
    /// The Tenor Id of this favorite
    /// </summary>
    public string TenorId { get; set; }

    /// <summary>
    /// The Id of the user this favorite belongs to
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Tries to add the given Tenor favorite
    /// </summary>
    public static async Task<TaskResult<TenorFavorite>> PostAsync(TenorFavorite favorite)
        => await ValourClient.PrimaryNode.PostAsyncWithResponse<TenorFavorite>(favorite.BaseRoute, favorite);

    /// <summary>
    /// Tries to delete the given Tenor favorite
    /// </summary>
    public static async Task<TaskResult> DeleteAsync(TenorFavorite favorite)
        => await ValourClient.PrimaryNode.DeleteAsync(favorite.IdRoute);

}