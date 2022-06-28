using System.Text.Json.Serialization;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;

namespace Valour.Server.Planets.Members;

/// <summary>
/// For getting data to the client.  Must match!
/// </summary>
public class PlanetMemberInfo
{
    [JsonPropertyName("Member")]
    public PlanetMember Member { get; set; }

    [JsonPropertyName("RoleIds")]
    public IEnumerable<ulong> RoleIds { get; set; }

    [JsonPropertyName("User")]
    public User User { get; set; }
}
