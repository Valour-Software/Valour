using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Models.Economy;
using Valour.Sdk.Models.Themes;

namespace Valour.Sdk.Services;

/// <summary>
/// The CacheService provides caching for the Valour client.
/// </summary>
public class CacheService
{
    /////////////
    // Lookups //
    /////////////
    
    public readonly Dictionary<DirectChannelKey, long> DmChannelKeyToId = new();
    public readonly Dictionary<PermissionsNodeKey, long> PermNodeKeyToId = new();
    public readonly Dictionary<PlanetMemberKey, long> MemberKeyToId = new();
    
    //////////////////
    // Model Caches // 
    //////////////////

    public readonly ModelCache<User, long> Users = new();
    public readonly ModelCache<EcoAccount, long> EcoAccounts = new();
    public readonly ModelCache<Currency, long> Currencies = new();
    public readonly ModelCache<Channel, long> Channels = new();
    public readonly ModelCache<UserFriend, long> UserFriends = new();
    public readonly ModelCache<UserProfile, long> UserProfiles = new();
    public readonly ModelCache<ChannelMember, long> ChannelMembers = new();
    public readonly ModelCache<Theme, long> Themes = new();
    public readonly ModelCache<OauthApp, long> OauthApps = new();
    
    public readonly ModelCache<Message, long> Messages = new();
    
    public readonly ModelCache<Planet, long> Planets = new();
    
    private readonly ValourClient _client;
    
    public CacheService(ValourClient client)
    {
        _client = client;
    }
}