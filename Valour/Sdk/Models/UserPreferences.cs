using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Sdk.Nodes;
using System.Text.Json.Serialization;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class UserPreferences : ClientModel<UserPreferences, long>, ISharedUserPreferences
{
    public override string BaseRoute => "api/users/me/preferences";

    [JsonIgnore]
    public override Node Node => Client?.AccountNode;

    public ErrorReportingState ErrorReportingState { get; set; }
    public int NotificationVolume { get; set; }
    public long EnabledNotificationSources { get; set; }
    public DmPolicy DmPolicy { get; set; }

    [JsonConstructor]
    private UserPreferences() : base() { }
    public UserPreferences(ValourClient client) : base(client) { }

    public override UserPreferences AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }

    public override UserPreferences RemoveFromCache(bool skipEvents = false)
    {
        return null;
    }
}
