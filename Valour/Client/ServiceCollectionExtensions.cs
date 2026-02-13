using Microsoft.Extensions.DependencyInjection;
using Valour.Client.Categories;
using Valour.Client.Components.Sidebar.Directory;
using Valour.Client.ContextMenu;
using Valour.Client.Sounds;
using Valour.Sdk.Client;
using Valour.Sdk.Services;

namespace Valour.Client;

public static class ServiceCollectionExtensions
{
    public static ValourClient AddValourClientServices(this IServiceCollection services, string baseAddress)
    {

        var loggingService = new LoggingService(false);
        var client = new ValourClient(baseAddress, loggingService);

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(client.BaseAddress),
        };

        client.SetHttpClient(httpClient);
        services.AddSingleton(httpClient);

        // old services TODO!
        services.AddSingleton<ClientCategoryManager>();
        services.AddSingleton<ChannelDragManager>();
        services.AddSingleton<SoundManager>();
        services.AddSingleton<ContextMenuService>();

        // new services
        services.AddSingleton(client);
        services.AddSingleton(client.Logger);
        services.AddSingleton(client.UserService);
        services.AddSingleton(client.Cache);
        services.AddSingleton(client.BotService);
        services.AddSingleton(client.AuthService);
        services.AddSingleton(client.ChannelStateService);
        services.AddSingleton(client.FriendService);
        services.AddSingleton(client.MessageService);
        services.AddSingleton(client.NodeService);
        services.AddSingleton(client.PlanetService);
        services.AddSingleton(client.ChannelService);
        services.AddSingleton(client.TenorService);
        services.AddSingleton(client.SubscriptionService);
        services.AddSingleton(client.NotificationService);
        services.AddSingleton(client.EcoService);
        services.AddSingleton(client.StaffService);
        services.AddSingleton(client.PermissionService);
        services.AddSingleton(client.OauthService);
        services.AddSingleton(client.SafetyService);
        services.AddSingleton(client.ThemeService);
        services.AddSingleton(client.UnreadService);
        services.AddSingleton(client.PlanetTagService);

        return client;
    }
}
