using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Valour.Sdk.Client;
using Valour.Client.Categories;
using Valour.Client.Components.Sidebar.ChannelList;
using Valour.Client.ContextMenu;
using Valour.Client.Sounds;
using Valour.Client.Tenor;

namespace Valour.Client.Blazor;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("app");

        builder.Services.AddBlazoredLocalStorage();

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(ValourClient.BaseAddress),
        };
        
        ValourClient.SetHttpClient(httpClient);

        builder.Services.AddScoped(sp =>
            httpClient
        );

        builder.Services.AddHttpClient<TenorService>(client =>
        {
            client.BaseAddress = new Uri("https://tenor.googleapis.com/v2/");
        });
        
        builder.Services.AddSingleton<ClientCategoryManager>();
        builder.Services.AddSingleton<ChannelListManager>();
        builder.Services.AddSingleton<SoundManager>();

        builder.Services.AddSingleton<ContextMenuService>();
        
        var host = builder.Build();

        var navService = host.Services.GetRequiredService<NavigationManager>();

        await host.RunAsync();
    }
}
