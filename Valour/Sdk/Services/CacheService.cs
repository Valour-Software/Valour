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
    public readonly ModelCache<PlanetMember, long> PlanetMembers = new();
    public readonly ModelCache<PlanetRole, long> PlanetRoles = new();
    public readonly ModelCache<PlanetBan, long> PlanetBans = new();
    public readonly ModelCache<PermissionsNode, long> PermissionsNodes = new();
    public readonly ModelCache<PlanetInvite, string> PlanetInvites = new(); 
    
    private readonly ValourClient _client;
    
    public CacheService(ValourClient client)
    {
        _client = client;
    }
    
    /// <summary>
    /// Pushes this version of this model to cache and optionally
    /// fires off event for the update. Flags can be added for additional data.
    /// Returns the global cached instance of the model.
    /// </summary>
    public TModel Sync<TModel>(TModel model, bool skipEvent = false, int flags = 0)
        where TModel : ClientModel<TModel>
    {
        if (model is null)
            return null;
        
        // Let the model know what client it is associated with
        model.SetClient(_client);

        model.SyncSubModels(skipEvent, flags);
        
        // Add to cache or get the existing cached instance
        var existing = model.AddToCacheOrReturnExisting();
        
        // Update the existing model with the new data, or broadcast the new item
        return ModelUpdater.UpdateItem<TModel>(model, existing, flags, skipEvent); // Update if already exists
    }
    
    /// <summary>
    /// Removes the model from cache and optionally fires off event for the deletion.
    /// </summary>
    public void Delete<TModel>(TModel model, bool skipEvent = false)
        where TModel : ClientModel<TModel>
    {
        if (model is null)
            return;
        
        // Remove from cache
        model.TakeAndRemoveFromCache();
        
        // Broadcast the deletion
        if (!skipEvent)
            ModelUpdater.DeleteItem<TModel>(model);
    }
}