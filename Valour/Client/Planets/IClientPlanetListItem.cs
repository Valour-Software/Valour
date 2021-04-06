using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared;
using Valour.Shared.Oauth;
using Valour.Shared.Roles;

namespace Valour.Client.Planets
{
    public interface IClientPlanetListItem
    {
        public ushort Position { get; set; }
        public ulong? Parent_Id { get; set; }
        public ulong Planet_Id { get; set; }
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public Task<TaskResult> SetNameAsync(string name);
        public Task<TaskResult> SetDescriptionAsync(string desc);

        public string GetItemTypeName();

        public Task<ClientPlanet> GetPlanetAsync();

        public Task<PermissionsNode> GetPermissionsNode(PlanetRole role);
    }
}
