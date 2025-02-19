namespace Valour.Shared.Models;

/// <summary>
/// A Tenor favorite is a Tenor gif saved by a user
/// </summary>
public interface ISharedTenorFavorite : ISharedModel<long>
{
    const string BaseRoute = "api/tenorfavorites";
    
    /// <summary>
    /// The Tenor Id of this favorite
    /// </summary>
    string TenorId { get; set; }
    
    /// <summary>
    /// The Id of the user this favorite belongs to
    /// </summary>
    long UserId { get; set; }
}