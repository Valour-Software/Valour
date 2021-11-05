using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Valour.Client.Categories;
using Microsoft.AspNetCore.Components;
using Valour.Client.Modals.ContextMenus;
using Valour.Client.Modals;
using Valour.Client.Shared.ChannelList;
using Valour.Api.Client;
using Blazored.Modal;


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

            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
            };

            ValourClient.SetHttpClient(httpClient);
            await ValourClient.InitializeSignalR(builder.HostEnvironment.BaseAddress.TrimEnd('/') + "/planethub");

            builder.Services.AddScoped(sp =>
                httpClient
            );

            builder.Services.AddSingleton<ClientWindowManager>();
            builder.Services.AddSingleton<ClientCategoryManager>();
            builder.Services.AddSingleton<ChannelListManager>();

            builder.Services.AddBlazoredModal();
            builder.Services.AddBlazorContextMenu(options =>
            {
                options.ConfigureTemplate("main", template =>
                {
                    template.Animation = BlazorContextMenu.Animation.FadeIn;
                    template.MenuCssClass = "context-menu";
                    template.MenuItemCssClass = "context-menu-item";
                });
            });

            // Context menus and modals
            builder.Services.AddSingleton<ChannelListContextMenu>();
            builder.Services.AddSingleton<AddChannelContextMenu>();

            var host = builder.Build();

            var navService = host.Services.GetRequiredService<NavigationManager>();

            await host.RunAsync();
        }
    }
} 
