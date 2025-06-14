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

    public readonly ModelStore<User, long> Users = new();
    public readonly ModelStore<EcoAccount, long> EcoAccounts = new();
    public readonly ModelStore<Currency, long> Currencies = new();
    public readonly ModelStore<Channel, long> Channels = new();
    public readonly ModelStore<UserFriend, long> UserFriends = new();
    public readonly ModelStore<UserProfile, long> UserProfiles = new();
    public readonly ModelStore<ChannelMember, long> ChannelMembers = new();
    public readonly ModelStore<Theme, long> Themes = new();
    public readonly ModelStore<OauthApp, long> OauthApps = new();
    public readonly ModelStore<Message, long> Messages = new();
    public readonly ModelStore<Planet, long> Planets = new();
    public readonly ModelStore<Tag, long> Tags = new();
    
    // For planets invites of planets the user is not a member of
    public readonly ModelStore<PlanetInvite, string> OutsidePlanetInvites = new();
    
    private readonly ValourClient _client;
    
    public CacheService(ValourClient client)
    {
        _client = client;
    }
}