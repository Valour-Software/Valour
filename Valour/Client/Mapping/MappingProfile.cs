using AutoMapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Categories;
using Valour.Client.Channels;
using Valour.Client.Planets;
using Valour.Client.Users;
using Valour.Shared.Categories;
using Valour.Shared.Channels;
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

            CreateMap<PlanetMember, ClientPlanetMember>();
            CreateMap<ClientPlanetMember, PlanetMember>();

            CreateMap<PlanetChatChannel, ClientPlanetChatChannel>();
            CreateMap<ClientPlanetChatChannel, PlanetChatChannel>();

            CreateMap<PlanetCategory, ClientPlanetCategory>();
            CreateMap<ClientPlanetCategory, PlanetCategory>();
        }
    }
}
