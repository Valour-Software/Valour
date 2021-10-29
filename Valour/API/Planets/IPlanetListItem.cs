using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Valour.Api.Authorization.Roles;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Roles;

namespace Valour.Api.Planets
{
    public interface IPlanetListItem
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

        [JsonPropertyName("Name")]
        public string Name { get; set; }

        [JsonPropertyName("ItemType")]
        public ItemType ItemType { get; }

        public Task<TaskResult> SetDescriptionAsync(string desc);
        public Task<TaskResult> SetNameAsync(string name);
        public Task<TaskResult> SetParentIdAsync(ulong? planet_id);
        public Task<TaskResult> DeleteAsync();
        public string GetItemTypeName();
        public Task<Planet> GetPlanetAsync();
        public Task<PermissionsNode> GetPermissionsNodeAsync(ulong role_id, bool force_refresh = false);
    }
}
