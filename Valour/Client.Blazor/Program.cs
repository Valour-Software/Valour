using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using Valour.Sdk.Client;
using Valour.Client.Categories;
using Valour.Client.Components.Sidebar.ChannelList;
using Valour.Client.ContextMenu;
using Valour.Client.Sounds;
using Valour.SDK.Services;
using TenorService = Valour.Client.Tenor.TenorService;

namespace Valour.Client.Blazor;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2024 Valour Software LLC
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

        var loggingService = new LoggingService(false);
        var client = new ValourClient("https://app.valour.gg", loggingService);

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(client.BaseAddress),
        };
        
        client.SetHttpClient(httpClient);

        builder.Services.AddSingleton(_ =>
            httpClient
        );

        builder.Services.AddHttpClient<TenorService>(tenorClient =>
        {
            tenorClient.BaseAddress = new Uri("https://tenor.googleapis.com/v2/");
        });
        
        // old services TODO!
        builder.Services.AddSingleton<ClientCategoryManager>();
        builder.Services.AddSingleton<ChannelListManager>();
        builder.Services.AddSingleton<SoundManager>();
        builder.Services.AddSingleton<ContextMenuService>();
        
        // new services
        builder.Services.AddSingleton(client);
        builder.Services.AddSingleton(client.Logger);
        builder.Services.AddSingleton(client.Cache);
        builder.Services.AddSingleton(client.BotService);
        builder.Services.AddSingleton(client.AuthService);
        builder.Services.AddSingleton(client.ChannelStateService);
        builder.Services.AddSingleton(client.FriendService);
        builder.Services.AddSingleton(client.MessageService);
        builder.Services.AddSingleton(client.NodeService);
        builder.Services.AddSingleton(client.PlanetService);
        builder.Services.AddSingleton(client.ChannelService);
        builder.Services.AddSingleton(client.TenorService);
        builder.Services.AddSingleton(client.SubscriptionService);
        builder.Services.AddSingleton(client.NotificationService);
        builder.Services.AddSingleton(client.EcoService);
        
        var host = builder.Build();
        
        var jsRuntime = host.Services.GetRequiredService<IJSRuntime>();
        
        await host.RunAsync();
    }
}
