using System.Text.Json.Serialization;
using Valour.Api.Items.Users;

namespace Valour.Api.Items.Planets.Members;

/// <summary>
/// For getting data from the server.  Must match!
/// </summary>
public class PlanetMemberInfo
{
    [JsonPropertyName("Member")]
    public PlanetMember Member { get; set; }

    [JsonPropertyName("RoleIds")]
    public List<ulong> RoleIds { get; set; }

    [JsonPropertyName("User")]
    public User User { get; set; }
}

