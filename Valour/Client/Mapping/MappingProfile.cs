using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Planets;
using Valour.Client.Users;
using Valour.Shared.Planets;
using Valour.Shared.Users;

namespace Valour.Client.Mapping
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Planet, ClientPlanet>();
            CreateMap<ClientPlanet, Planet>();

            CreateMap<PlanetUser, ClientPlanetUser>();
            CreateMap<ClientPlanetUser, PlanetUser>();
        }
    }
}
