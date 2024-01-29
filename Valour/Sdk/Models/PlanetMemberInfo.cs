using System.Text.Json.Serialization;

namespace Valour.Api.Models;

/// <summary>
/// For getting data from the server.  Must match!
/// </summary>
public class PlanetMemberInfo
{
    public int TotalCount { get; set; }
    public PlanetMemberData[] Members { get; set; }
}

public class PlanetMemberData
{
    public PlanetMember Member { get; set; }
    public List<long> RoleIds { get; set; }
    public User User { get; set; }
}

