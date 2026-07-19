namespace Valour.Shared.Models;

/// <summary>
/// A GIF favorite saved by a user. Provider data is stored with the favorite
/// so old favorites stay usable even when a provider removes an item.
/// </summary>
public interface ISharedGifFavorite : ISharedModel<long>
{
    const string BaseRoute = "api/giffavorites";

    long UserId { get; set; }
    string Provider { get; set; }
    string ProviderId { get; set; }
    string Title { get; set; }
    string PreviewUrl { get; set; }
    string GifUrl { get; set; }
    int Width { get; set; }
    int Height { get; set; }
}
