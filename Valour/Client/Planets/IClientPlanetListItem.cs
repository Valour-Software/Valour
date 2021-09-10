using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Roles;

namespace Valour.Client.Planets
{
    public interface IClientPlanetListItem : IClientNamedItem
    {
        [JsonInclude]
        [JsonPropertyName("Id")]
        public ulong Id { get; set; }

        [JsonPropertyName("Parent_Id")]
        public ulong? Parent_Id { get; set; }

        [JsonPropertyName("Position")]
        public ushort Position { get; set; }

        [JsonPropertyName("Planet_Id")]
        public ulong Planet_Id { get; set; }

        [JsonPropertyName("Description")]
        public string Description { get; set; }



        public Task<TaskResult> TrySetDescriptionAsync(string desc);
        public Task<TaskResult> TrySetParentIdAsync(ulong planet_id);
        public Task<TaskResult> TryDeleteAsync(); 
        public string GetItemTypeName();
        public Task<ClientPlanet> GetPlanetAsync();
        public Task<PermissionsNode> GetPermissionsNodeAsync(PlanetRole role);
    }
}
