using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Planets;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Server.Users;
using Valour.Shared.Planets;
using Valour.Shared.Roles;
using Valour.Shared.Users;

namespace Valour.Server.Mapping
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Planet, ServerPlanet>();
            CreateMap<ServerPlanet, Planet>();

            CreateMap<PlanetMember, ServerPlanetMember>();
            CreateMap<ServerPlanetMember, PlanetMember>();

            CreateMap<PlanetRole, ServerPlanetRole>();
            CreateMap<ServerPlanetRole, PlanetRole>();

            CreateMap<ClientPlanetMember, ServerPlanetMember>();
            CreateMap<ServerPlanetMember, ClientPlanetMember>();
        }
    }
}
