using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// State needed immediately after login. Returning it as one document avoids
/// a burst of cross-origin requests (and one CORS preflight per route).
/// </summary>
public sealed class AppBootstrapData
{
    public UserFriendData FriendData { get; set; } = new();
    public List<UserBlock> Blocks { get; set; } = [];
    public List<Planet> Planets { get; set; } = [];
    public List<PlanetMember> MyPlanetMembers { get; set; } = [];
    public List<FederatedMembershipInfo> FederatedMemberships { get; set; } = [];
    public List<GifFavorite> GifFavorites { get; set; } = [];
    public List<Channel> DirectChannels { get; set; } = [];
    public List<Notification> UnreadNotifications { get; set; } = [];
    public long[] UnreadPlanets { get; set; } = [];
    public long[] UnreadDirectChannels { get; set; } = [];
    public UserPreferences Preferences { get; set; }
}
