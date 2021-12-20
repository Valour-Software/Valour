using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets.Members;

namespace Valour.Api.Items.Planets.Members;

public class PlanetRoleMember : Item<PlanetRoleMember>, ISharedPlanetRoleMember
{
    [JsonPropertyName("User_Id")]
    public ulong User_Id { get; set; }

    [JsonPropertyName("Role_Id")]
    public ulong Role_Id { get; set; }

    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    [JsonPropertyName("Member_Id")]
    public ulong Member_Id { get; set; }

    [JsonPropertyName("ItemType")]
    public override ItemType ItemType => ItemType.PlanetRoleMember;
}

