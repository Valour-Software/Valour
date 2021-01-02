using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Planets;
using Valour.Shared.Planets;

namespace Valour.Server.Mapping
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Planet, ServerPlanet>();
            CreateMap<ServerPlanet, Planet>();
        }
    }
}
