using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using Valour.Api.Client;
using Valour.Shared;
using Valour.Shared.Items;

namespace Valour.Client.Planets
{
    /// <summary>
    /// Common client functionality for named items
    /// </summary>
    public interface IClientNamedItem
    {
        [JsonInclude]
        [JsonPropertyName("Id")]
        public ulong Id { get; }

        [JsonInclude]
        [JsonPropertyName("ItemType")]
        public ItemType ItemType { get; }

        [JsonInclude]
        [JsonPropertyName("Name")]
        public string Name { get; set; }

        public async Task<TaskResult> TrySetNameAsync(string name) =>
            await ValourClient.PutAsync($"api/{ItemType}/{Id}/name", name);
    }
}
