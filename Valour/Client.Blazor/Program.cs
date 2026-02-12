using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Valour.Client;
using Valour.Client.Notifications;
using Valour.Client.Storage;

namespace Valour.Client.Blazor;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("app");
        
        builder.UseSentry(options =>
        {
            options.Dsn = "https://6cfc20b598b8831b69f8a30629325213@o4510867505479680.ingest.us.sentry.io/4510869629435904";
            options.MinimumEventLevel = LogLevel.Error;
        });
        
        builder.Services.AddSingleton<IAppStorage, BrowserStorageService>();
        builder.Services.AddSingleton<IPushNotificationService, BrowserPushNotificationService>();
        builder.Services.AddValourClientServices("https://app.valour.gg");
        
        var host = builder.Build();
        await host.RunAsync();
    }
}
