using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using AutoMapper;
using Valour.Client.Mapping;
using Microsoft.JSInterop;
using Valour.Client.Planets;


/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Client
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("app");

            builder.Services.AddBlazoredLocalStorage();
            builder.Services.AddScoped<LocalStorageService>();
            builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
            builder.Services.AddSingleton<ClientPlanetManager>();
            builder.Services.AddSingleton<ClientWindowManager>();


            var mapConfig = new MapperConfiguration(x =>
            {
                x.AddProfile(new MappingProfile());
            });

            IMapper mapper = mapConfig.CreateMapper();

            builder.Services.AddSingleton(mapper);

            await builder.Build().RunAsync();
        }
    }
} 
