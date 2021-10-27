using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Valour.Client.Categories;
using Microsoft.AspNetCore.Components;
using Valour.Client.Modals.ContextMenus;
using Valour.Client.Modals;
using Valour.Client.Shared.ChannelList;


/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
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
            builder.Services.AddScoped(sp =>
                new HttpClient
                {
                    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
                }
            );

            builder.Services.AddSingleton<ClientWindowManager>();
            builder.Services.AddSingleton<ClientCategoryManager>();
            builder.Services.AddSingleton<ChannelListManager>();

            // Context menus and modals
            builder.Services.AddSingleton<MemberContextMenu>();
            builder.Services.AddSingleton<ChannelListContextMenu>();
            builder.Services.AddSingleton<AddChannelContextMenu>();
            builder.Services.AddSingleton<ConfirmModal>();
            builder.Services.AddSingleton<InfoModal>();
            builder.Services.AddSingleton<BanModal>();
            builder.Services.AddSingleton<EditPlanetModal>();
            builder.Services.AddSingleton<EditUserModal>();
            builder.Services.AddSingleton<CreateChannelModal>();
            builder.Services.AddSingleton<CreateCategoryModal>();
            builder.Services.AddSingleton<CreatePlanetModal>();
            builder.Services.AddSingleton<EditChannelListItemModal>();

            var host = builder.Build();

            var navService = host.Services.GetRequiredService<NavigationManager>();

            await host.RunAsync();
        }
    }
} 
