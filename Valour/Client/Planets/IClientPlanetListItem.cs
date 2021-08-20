using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Web;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Roles;

namespace Valour.Client.Planets
{
    public interface IClientPlanetListItem : IChannelListItem, IClientNamedItem
    {
        public Task<TaskResult> TrySetDescriptionAsync(string desc);
        public Task<TaskResult> TrySetParentIdAsync(ulong planet_id);
        public Task<TaskResult> TryDeleteAsync(); 
        public string GetItemTypeName();
        public Task<ClientPlanet> GetPlanetAsync();
        public Task<PermissionsNode> GetPermissionsNodeAsync(PlanetRole role);
    }
}
