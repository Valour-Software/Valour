using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using CloudFlare.Client;
using StackExchange.Redis;
using Valour.Server.API;
using Valour.Server.Cdn;
using Valour.Server.Cdn.Api;
using Valour.Server.Cdn.Extensions;
using Valour.Server.Cdn.Storage;
using Valour.Server.Email;
using Valour.Server.Redis;
using Valour.Server.Workers;
using Valour.Shared.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Valour.Config;
using Microsoft.AspNetCore.Components;
using Microsoft.OpenApi;
using Valour.Config.Configs;
using Valour.Server.Api.Dynamic;
using Valour.Server.Hubs;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
namespace Valour.Server;

public partial class Program
{
    public static List<object> DynamicApis { get; set; }

    public static NodeAPI NodeAPI { get; set; }
    
    public static async Task Main(string[] args)
    {
        // Create builder
        var builder = WebApplication.CreateBuilder(args);

        // Dev on linux will literally explode without this. Took a fun 5 hours to figure out.
        builder.WebHost.UseStaticWebAssets();

        // Load configs
        ConfigLoader.LoadConfigs();

        // Propagate configured hosts to the shared source of truth used by
        // shared models and the database layer.
        ValourHosts.RootDomain = HostingConfig.Current.RootDomain;
        ValourHosts.AppHost = HostingConfig.Current.AppHost;
        ValourHosts.ThreadsHost = HostingConfig.Current.ThreadsHost;
        ValourHosts.WikiHost = HostingConfig.Current.WikiHost;
        ValourHosts.ContentCdnHost = HostingConfig.Current.ContentCdnHost;
        ValourHosts.PublicCdnHost = HostingConfig.Current.PublicCdnHost;

        // Initialize Stripe
        if (StripeConfig.Current?.SecretKey is not null)
            Stripe.StripeConfiguration.ApiKey = StripeConfig.Current.SecretKey;
        if (!string.IsNullOrWhiteSpace(StripeConfig.Current?.ApiVersion))
        {
            Console.WriteLine(
                $"Stripe ApiVersion override '{StripeConfig.Current.ApiVersion}' requested in config, " +
                $"but Stripe.net pins request API version to '{Stripe.StripeConfiguration.ApiVersion}'. " +
                "Set webhook endpoint/CLI API version on Stripe side.");
        }

        // Initialize Email Manager
        EmailManager.SetupClient();

        // Initialize Firebase for FCM push notifications
        if (!string.IsNullOrWhiteSpace(NotificationsConfig.Current?.FirebaseCredentialPath))
        {
            FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromFile(NotificationsConfig.Current.FirebaseCredentialPath)
            });
        }

#if !DEBUG
            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                options.Configure(builder.Configuration.GetSection("Kestrel"));
                options.Listen(IPAddress.Any, 5000, listenOptions =>
                {
                    listenOptions.Protocols =
 Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3;
                });
            });
#endif


        if (builder.Configuration.GetSection("Sentry").Exists())
        {
            builder.WebHost.UseSentry(x =>
            {
                x.Release = typeof(ISharedUser).Assembly.GetName().Version.ToString();
                x.ServerName = NodeConfig.Instance.Name;
            });
        }

        // Set up services
        ConfigureServices(builder);

        // Build web app
        var app = builder.Build();

        // Configure application
        ConfigureApp(app);

        app.MapGet("/api/ping", () => "pong");
        

        // Add Cdn routes
        ContentApi.AddRoutes(app);
        UploadApi.AddRoutes(app);
        ProxyApi.AddRoutes(app);

        // In filesystem storage mode the server itself serves public assets
        // (avatars, icons, emoji) under /valour-public/; in s3 mode an external
        // public CDN host serves them.
        if (app.Services.GetRequiredService<CdnStorageProvider>().Mode == CdnStorageMode.FileSystem)
        {
            PublicContentApi.AddRoutes(app);
        }

        // Add API routes
        BaseAPI.AddRoutes(app);
        InstanceApi.AddRoutes(app);
        EmbedAPI.AddRoutes(app);
        OauthAppApi.StartCodeCleanupTask();
        VoiceSignallingApi.AddRoutes(app);
        
        DynamicApis = new()
        {
            new DynamicAPI<UserApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetStorageApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetVoiceApi>().RegisterRoutes(app),
            new DynamicAPI<FederationApi>().RegisterRoutes(app),
            new DynamicAPI<ChannelApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetMemberApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetRoleApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetEmojiApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetRuleApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetReportApi>().RegisterRoutes(app),
            new DynamicAPI<ThreadApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetWikiApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetInviteApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetBanApi>().RegisterRoutes(app),
            new DynamicAPI<PermissionsNodeApi>().RegisterRoutes(app),
            new DynamicAPI<AutomodApi>().RegisterRoutes(app),
            new DynamicAPI<UserFriendApi>().RegisterRoutes(app),
            new DynamicAPI<UserBlockApi>().RegisterRoutes(app),
            new DynamicAPI<OauthAppApi>().RegisterRoutes(app),
            new DynamicAPI<TenorFavoriteApi>().RegisterRoutes(app),
            new DynamicAPI<EcoApi>().RegisterRoutes(app),
            new DynamicAPI<NotificationApi>().RegisterRoutes(app),
            new DynamicAPI<ReportApi>().RegisterRoutes(app),
            new DynamicAPI<UserProfileApi>().RegisterRoutes(app),
            new DynamicAPI<SubscriptionApi>().RegisterRoutes(app),
            new DynamicAPI<StripeApi>().RegisterRoutes(app),
            new DynamicAPI<ThemeApi>().RegisterRoutes(app),
            new DynamicAPI<StaffApi>().RegisterRoutes(app),
            new DynamicAPI<MessageApi>().RegisterRoutes(app),
            new DynamicAPI<AttachmentApi>().RegisterRoutes(app),
            new DynamicAPI<UnreadApi>().RegisterRoutes(app),
            new DynamicAPI<TagApi>().RegisterRoutes(app),
            new DynamicAPI<BotApi>().RegisterRoutes(app),
            new DynamicAPI<UnsubscribeApi>().RegisterRoutes(app),
        };

        NodeAPI = new NodeAPI();
        NodeAPI.AddRoutes(app);

        // Migrations and tasks

        // Remove old connections for this node since we have restarted
        var redis = app.Services.GetRequiredService<IConnectionMultiplexer>();
        var rdb = redis.GetDatabase(RedisDbTypes.Cluster);

        foreach (var con in rdb.SetScan($"node:{NodeConfig.Instance.Name}"))
        {
            var split = con.ToString().Split(':');
            var userIdString = split[0];
            var conIdString = split[1];
            await rdb.SetRemoveAsync($"user:{userIdString}", $"{NodeConfig.Instance.Name}:{conIdString}",
                CommandFlags.FireAndForget);
        }

        await app.RunAsync();
    }

    public static void ConfigureApp(WebApplication app)
    {
        app.UseCors("AllowAll");

        if (app.Environment.IsDevelopment())
        {
            app.UseWebAssemblyDebugging();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseSwagger();

        app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1"); });

        app.UseWebSockets();

        if (app.Configuration.GetSection("Sentry").Exists())
        {
            app.UseSentryTracing();
        }

        app.UseBlazorFrameworkFiles();
        app.MapStaticAssets();

        // Clean URLs on the threads subdomain (threads.valour.gg/{planetId}/{threadId})
        // are rewritten to the underlying /threads/... Razor pages.
        var threadsHost = HostingConfig.Current.ThreadsHost;
        app.Use(async (context, next) =>
        {
            if (string.Equals(context.Request.Host.Host, threadsHost, StringComparison.OrdinalIgnoreCase) &&
                !context.Request.Path.StartsWithSegments("/threads", StringComparison.OrdinalIgnoreCase))
            {
                var segments = context.Request.Path.Value?
                    .Split('/', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

                if (segments.Length is 1 or 2 && segments.All(s => long.TryParse(s, out _)))
                    context.Request.Path = "/threads/" + string.Join('/', segments);
            }

            await next();
        });

        // Clean URLs on the docs subdomain (wiki.valour.gg/{planetIdOrVanity}/{pageSlug})
        // are rewritten to the underlying /docs/... Razor pages. Only active when
        // the docs host is distinct — in single-domain self-host mode the docs
        // pages stay at the /docs/... path so bare /{vanity} paths can never
        // swallow app routes.
        var wikiHost = HostingConfig.Current.WikiHost;
        var wikiHostDistinct =
            !string.Equals(wikiHost, HostingConfig.Current.AppHost, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(wikiHost, HostingConfig.Current.RootDomain, StringComparison.OrdinalIgnoreCase);
        if (wikiHostDistinct)
        {
            app.Use(async (context, next) =>
            {
                if (string.Equals(context.Request.Host.Host, wikiHost, StringComparison.OrdinalIgnoreCase) &&
                    !context.Request.Path.StartsWithSegments("/wiki", StringComparison.OrdinalIgnoreCase) &&
                    !context.Request.Path.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase) &&
                    !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
                {
                    if (context.Request.Path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("User-agent: *\nAllow: /\n");
                        return;
                    }

                    var segments = context.Request.Path.Value?
                        .Split('/', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

                    if (segments.Length is 1 or 2 && segments.All(IsWikiRewriteSegment))
                        context.Request.Path = "/wiki/" + string.Join('/', segments);
                }

                await next();
            });
        }

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();
        app.MapControllers();
        app.MapBlazorHub();

        app.MapFallbackToFile("_content/Valour.Client/index.html");

        app.MapHub<CoreHub>(CoreHub.HubUrl, options => { options.AllowStatefulReconnects = true; });

        app.MapGet("/healthz", async (ValourDb db, CancellationToken ct) =>
        {
            if (!await db.Database.CanConnectAsync(ct))
            {
                return ValourResult.Problem("db not ready");
            }

            return ValourResult.Ok("ready");
        });
    }

    /// <summary>
    /// CORS origins derived from the configured hosting domains, plus fixed
    /// dev and third-party entries. Note: the policy also sets
    /// SetIsOriginAllowed(_ => true), which currently supersedes this list.
    /// </summary>
    /// <summary>
    /// True for docs clean-URL segments: planet ids, vanity names, page slugs
    /// (letters/digits/dashes), or the literal sitemap.xml.
    /// </summary>
    private static bool IsWikiRewriteSegment(string segment)
    {
        if (string.Equals(segment, "sitemap.xml", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrEmpty(segment) || segment.Length > 64)
            return false;

        foreach (var c in segment)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                return false;
        }

        return true;
    }

    private static string[] BuildCorsOrigins()
    {
        var hosting = HostingConfig.Current;
        return
        [
            $"https://{hosting.AppHost}",
            $"http://{hosting.AppHost}",
            $"https://www.{hosting.RootDomain}",
            $"http://www.{hosting.RootDomain}",
            $"https://{hosting.RootDomain}",
            $"http://{hosting.RootDomain}",
            $"https://{hosting.ApiHost}",
            $"http://{hosting.ApiHost}",
            "https://tenor.googleapis.com",
            "http://localhost:3000",
            "https://localhost:3000",
            "http://localhost:3001",
            "https://localhost:3001",
            "http://localhost:5000",
            "http://localhost:5001",
        ];
    }

    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", builder =>
            {
                builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .SetIsOriginAllowed(_ => true)
                    .AllowCredentials()
                    .WithOrigins(BuildCorsOrigins());
            });
        });
        
        services.AddMemoryCache();
        
        services.AddSignalR(options =>
        {
            options.MaximumParallelInvocationsPerClient = 5;
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient();

        services.Configure<FormOptions>(options =>
        {
            options.MemoryBufferThreshold = 20_971_520; // 20 MB memory buffer before spilling to disk
            options.MultipartBodyLengthLimit = 262_144_000; // 250 MB (max tier upload limit)
        });

        services.AddDbContext<ValourDb>(options => { options.UseNpgsql(ValourDb.ConnectionString); }, ServiceLifetime.Scoped);

        // Apply migrations if flag is set
        //if (Environment.GetEnvironmentVariable("APPLY_MIGRATIONS") == "true")
        //{
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            db.Database.Migrate();
        //}
        
        Console.WriteLine("Connecting to redis with connection string: " + RedisConfig.Current.ConnectionString?.Split(",")[0]);
        
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(RedisConfig.Current.ConnectionString));

        // This probably needs to be customized further but the documentation changed
        services.AddAuthentication().AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);


        services.AddControllersWithViews().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            //options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
            options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;

            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        services.AddRazorPages();
        services.AddServerSideBlazor();

        // CloudFlareClient throws on an empty token, but the client must always
        // be registered (CdnBucketService depends on it). When no key is
        // configured we pass a placeholder — cache purges are skipped when no
        // zone is configured, so the placeholder client is never actually used.
        services.AddSingleton<ICloudFlareClient>(provider =>
            new CloudFlareClient(string.IsNullOrWhiteSpace(CloudflareConfig.Instance?.ApiKey)
                ? "unconfigured"
                : CloudflareConfig.Instance.ApiKey));

        // Data Protection keys live in the shared DB so every node (and
        // container restarts) can decrypt protected payloads such as planet
        // storage credentials.
        services.AddDataProtection()
            .SetApplicationName("Valour")
            .PersistKeysToDbContext<ValourDb>();

        services.AddSingleton<CdnStorageProvider>();
        services.AddSingleton<CdnBucketService>();

        services.AddHttpClient<ProxyHandler>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ValourCDN/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false
        });

        services.AddHttpClient("ProxyFetch", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ValourCDN/1.0");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false
        });

        services.AddHttpClient("PhotoDNA", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ValourSafety/1.0");
        });

        services.AddSingleton<SignalRConnectionService>();
        services.AddSingleton<UserOnlineQueueService>();

        services.AddSingleton<CdnMemoryCache>();
        services.AddSingleton<ModelCacheService>();
        services.AddSingleton<UserCacheService>();
        services.AddSingleton<RealtimeKitService>();
        services.AddSingleton<LiveKitService>();
        // The instance-wide backend is resolved from config: an explicit Voice__Provider
        // wins; otherwise LiveKit is auto-selected only when it is configured and
        // RealtimeKit is not, so the managed default is never silently changed.
        // The coordinator wraps it to route bring-your-own-voice planets to their
        // own SFUs per channel, and is what the rest of the server sees.
        services.AddSingleton<VoiceCoordinator>(sp =>
        {
            var cf = CloudflareConfig.Instance;
            var realtimeKitConfigured =
                !string.IsNullOrWhiteSpace(cf?.RealtimeAccountId) &&
                !string.IsNullOrWhiteSpace(cf?.RealtimeAppId) &&
                !string.IsNullOrWhiteSpace(cf?.RealtimeApiToken);

            IVoiceProvider instanceProvider =
                VoiceProviderSelector.Resolve(VoiceConfig.Current, realtimeKitConfigured) == VoiceProvider.LiveKit
                    ? sp.GetRequiredService<LiveKitService>()
                    : sp.GetRequiredService<RealtimeKitService>();

            return new VoiceCoordinator(
                instanceProvider,
                sp.GetRequiredService<LiveKitService>(),
                sp,
                sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(),
                sp.GetRequiredService<ILogger<VoiceCoordinator>>());
        });
        services.AddSingleton<IVoiceProvider>(sp => sp.GetRequiredService<VoiceCoordinator>());
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        services.AddScoped<HostedPlanetService>();

        services.AddScoped<UserOnlineService>();
        services.AddScoped<CoreHubService>();
        services.AddScoped<ChannelWatchingService>();
        services.AddScoped<CurrentlyTypingService>();
        services.AddScoped<OauthAppService>();
        services.AddScoped<PermissionsNodeService>();
        services.AddScoped<MultiAuthService>();

        services.AddScoped<OauthAppService>();
        services.AddScoped<PlanetBanService>();
        services.AddScoped<ChatCacheService>();
        services.AddScoped<ChannelService>();
        services.AddScoped<MessageService>();
        services.AddScoped<PlanetStorageService>();
        services.AddScoped<PlanetVoiceService>();
        services.AddScoped<FederationKeyService>();
        services.AddScoped<FederationHubService>();
        services.AddScoped<FederationNodeService>();
        services.AddScoped<FederationPlanetRegistryService>();
        services.AddScoped<FederationNodeClient>();
        services.AddScoped<PlanetSnapshotService>();
        services.AddScoped<FederationMigrationService>();
        services.AddScoped<FederationPurgeService>();
        services.AddScoped<FederationJoinService>();

        // Federation S2S/JWKS fetches. Insecure mode (dev/LAN clone networks)
        // accepts self-signed certificates.
        // Federation S2S/JWKS fetches to attacker-influenced node domains.
        // The connect callback resolves and connects to a validated public IP,
        // closing the DNS-rebinding window (the fetch cannot re-resolve to an
        // internal address after a name-based check). Insecure mode (dev/LAN
        // clone networks) allows private targets and self-signed certificates.
        var federationInsecure = FederationConfig.Current?.AllowInsecure == true;
        services.AddHttpClient("federation", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        }).ConfigurePrimaryHttpMessageHandler(() =>
            SsrfSafeConnect.CreateHandler(
                allowPrivate: federationInsecure,
                acceptAnyCertificate: federationInsecure));
        services.AddScoped<UserAttachmentService>();
        services.AddScoped<MediaSafetyService>();
        services.AddScoped<PlanetInviteService>();
        services.AddScoped<PlanetMemberService>();
        services.AddScoped<PlanetRoleService>();
        services.AddScoped<PlanetEmojiService>();
        services.AddScoped<PlanetRuleService>();
        services.AddScoped<PlanetReportService>();
        services.AddScoped<ThreadService>();
        services.AddScoped<PlanetWikiService>();
        services.AddScoped<PlanetService>();
        services.AddScoped<TenorFavoriteService>();
        services.AddScoped<AutomodService>();
        services.AddScoped<ModerationAuditService>();
        services.AddScoped<BotService>();
        services.AddScoped<TokenService>();
        services.AddScoped<UserFriendService>();
        services.AddScoped<UserBlockService>();
        services.AddScoped<UserService>();
        services.AddScoped<UnreadService>();
        services.AddScoped<EcoService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<ReportService>();
        services.AddScoped<RegisterService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<ThemeService>();
        services.AddScoped<StaffService>();
        services.AddScoped<PlanetPermissionService>();
        services.AddScoped<VoiceStateService>();
        services.AddScoped<StartupService>();
        services.AddScoped<PushNotificationService>();
        services.AddScoped<ITagService,TagService>();

        services.AddHttpClient<DiscordImportService>();

        services.AddSingleton<NodeLifecycleService>();
        
        // Register PushNotificationWorker as a singleton.
        services.AddSingleton<PushNotificationWorker>();
        // Register it as the IHostedService.
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<PushNotificationWorker>());

        services.AddHostedService<PlanetMessageWorker>();
        services.AddHostedService<StatWorker>();
        services.AddHostedService<ChannelWatchingWorker>();
        services.AddHostedService<FederationPurgeWorker>();
        services.AddHostedService<UserOnlineWorker>();
        services.AddHostedService<NodeStateWorker>();
        services.AddHostedService<SubscriptionWorker>();
        services.AddHostedService<StripeReconciliationWorker>();
        services.AddHostedService<VoiceStateCleanupWorker>();
        services.AddHostedService<HostedPlanetCleanupWorker>();
        services.AddHostedService<NotificationCleanupWorker>();
        services.AddHostedService<MigrationWorker>();
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1",
                new OpenApiInfo { Title = "Valour API", Description = "The official Valour API", Version = "v1.0" });
            c.AddSecurityDefinition("Token", new OpenApiSecurityScheme()
            {
                Description = "The token used for authorizing your account.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Token"
            });
            c.OperationFilter<FileUploadOperation>();
        });
    }
}
