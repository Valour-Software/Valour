using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json;
using Amazon;
using CloudFlare.Client;
using StackExchange.Redis;
using Valour.Server.API;
using Valour.Server.Cdn;
using Valour.Server.Cdn.Api;
using Valour.Server.Cdn.Extensions;
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
using Valour.Server.Middleware;
using WebOptimizer;

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

        // Initialize Email Manager
        EmailManager.SetupClient();

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

        // Add API routes
        BaseAPI.AddRoutes(app);
        EmbedAPI.AddRoutes(app);
        OauthAPI.AddRoutes(app);
        
        // s3 (r2) setup
        
        //AWSConfigsS3.UseSignatureVersion4 = true;

        if (CdnConfig.Current is not null)
        {
            // private bucket
            BasicAWSCredentials cred = new(CdnConfig.Current.S3Access, CdnConfig.Current.S3Secret);
            var config = new AmazonS3Config()
            {
                ServiceURL = CdnConfig.Current.S3Endpoint
            };

            AmazonS3Client client = new(cred, config);
            CdnBucketService.Client = client;
            
            // public bucket
            BasicAWSCredentials publicCred = new(CdnConfig.Current.PublicS3Access, CdnConfig.Current.PublicS3Secret);
            var publicConfig = new AmazonS3Config()
            {
                ServiceURL = CdnConfig.Current.PublicS3Endpoint
            };
            
            AmazonS3Client publicClient = new(publicCred, publicConfig);
            CdnBucketService.PublicClient = publicClient;
        }
        else
        {
            Console.WriteLine("Missing CDN config - file uploads will not function properly");
        }

        DynamicApis = new()
        {
            new DynamicAPI<UserApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetApi>().RegisterRoutes(app),
            new DynamicAPI<ChannelApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetMemberApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetRoleApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetInviteApi>().RegisterRoutes(app),
            new DynamicAPI<PlanetBanApi>().RegisterRoutes(app),
            new DynamicAPI<PermissionsNodeApi>().RegisterRoutes(app),
            new DynamicAPI<AutomodApi>().RegisterRoutes(app),
            new DynamicAPI<UserFriendApi>().RegisterRoutes(app),
            new DynamicAPI<OauthAppApi>().RegisterRoutes(app),
            new DynamicAPI<TenorFavoriteApi>().RegisterRoutes(app),
            new DynamicAPI<EcoApi>().RegisterRoutes(app),
            new DynamicAPI<NotificationApi>().RegisterRoutes(app),
            new DynamicAPI<ReportApi>().RegisterRoutes(app),
            new DynamicAPI<UserProfileApi>().RegisterRoutes(app),
            new DynamicAPI<SubscriptionApi>().RegisterRoutes(app),
            new DynamicAPI<OrderApi>().RegisterRoutes(app),
            new DynamicAPI<ThemeApi>().RegisterRoutes(app),
            new DynamicAPI<StaffApi>().RegisterRoutes(app),
            new DynamicAPI<MessageApi>().RegisterRoutes(app),
            new DynamicAPI<UnreadApi>().RegisterRoutes(app),
            new DynamicAPI<TagApi>().RegisterRoutes(app),
            new DynamicAPI<BotApi>().RegisterRoutes(app)
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

        // Add CSS injector middleware
        app.UseMiddleware<BlazorCssInjector>();
        
        app.UseWebSockets();

        if (app.Configuration.GetSection("Sentry").Exists())
        {
            app.UseSentryTracing();
        }

        
        // app.UseStartupWait();

        // app.UseBlazorCssMinifier();
        // app.UseWebOptimizer();
        app.UseBlazorFrameworkFiles();
        app.MapStaticAssets();
        
        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();
        app.MapControllers();
        app.MapBlazorHub();

        app.MapFallbackToFile("_content/Valour.Client/index.html");

        app.MapHub<CoreHub>(CoreHub.HubUrl, options => { options.AllowStatefulReconnects = true; });
        
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
                    .WithOrigins(
                        "https://www.valour.gg",
                        "https://tenor.googleapis.com",
                        "http://www.valour.gg",
                        "https://valour.gg",
                        "http://valour.gg",
                        "https://api.valour.gg",
                        "http://api.valour.gg",
                        "http://localhost:3000",
                        "https://localhost:3000",
                        "http://localhost:3001",
                        "http://localhost:5001",
                        "http://localhost:5000",
                        "https://localhost:3001");
            });
        });
        
        services.AddMemoryCache();
        
        services.AddWebOptimizer(pipeline =>
        {
            // Helper function to configure CSS bundles
            void ConfigureBundle(IAsset bundle)
            {
                if (true || !builder.Environment.IsDevelopment())
                {
                    bundle.MinifyCss()
                        .AdjustRelativePaths()
                        .UseContentRoot();
                }
            }
            
            // Blazor scoped CSS - use specific route but glob pattern for sources
            var scopedCssBundle = pipeline.AddCssBundle(
                "/_content/Valour.Client/Valour.Client.styles.css", // Fixed route
                "/_content/**/*.bundle.scp.css" // Source files can use glob pattern
            );
            
            ConfigureBundle(scopedCssBundle);
        });



        services.AddSignalR(options =>
        {
            options.MaximumParallelInvocationsPerClient = 5;
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        });

        services.AddHttpClient();

        services.Configure<FormOptions>(options =>
        {
            options.MemoryBufferThreshold = 20480000;
            options.MultipartBodyLengthLimit = 20480000;
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

        //if (!string.IsNullOrEmpty(CloudflareConfig.Instance?.ApiKey))
        //{
            services.AddSingleton<ICloudFlareClient>(provider =>
                new CloudFlareClient(CloudflareConfig.Instance?.ApiKey ?? string.Empty));
        //}

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

        services.AddSingleton<SignalRConnectionService>();
        services.AddSingleton<UserOnlineQueueService>();

        services.AddSingleton<CdnMemoryCache>();
        services.AddSingleton<ModelCacheService>();
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        services.AddScoped<HostedPlanetService>();

        services.AddScoped<UserOnlineService>();
        services.AddScoped<CoreHubService>();
        services.AddScoped<CurrentlyTypingService>();
        services.AddScoped<OauthAppService>();
        services.AddScoped<PermissionsNodeService>();
        services.AddScoped<MultiAuthService>();

        services.AddScoped<OauthAppService>();
        services.AddScoped<PlanetBanService>();
        services.AddScoped<ChatCacheService>();
        services.AddScoped<ChannelService>();
        services.AddScoped<MessageService>();
        services.AddScoped<PlanetInviteService>();
        services.AddScoped<PlanetMemberService>();
        services.AddScoped<PlanetRoleService>();
        services.AddScoped<PlanetService>();
        services.AddScoped<TenorFavoriteService>();
        services.AddScoped<AutomodService>();
        services.AddScoped<BotService>();
        services.AddScoped<TokenService>();
        services.AddScoped<UserFriendService>();
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
        services.AddScoped<StartupService>();
        services.AddScoped<PushNotificationService>();
        services.AddScoped<ITagService,TagService>();

        services.AddHttpClient<DiscordImportService>();

        services.AddSingleton<NodeLifecycleService>();
        
        // Add the CSS bundling service
        // builder.Services.AddSingleton<BlazorCssBundleService>();
        
        // Register PushNotificationWorker as a singleton.
        services.AddSingleton<PushNotificationWorker>();
        // Register it as the IHostedService.
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<PushNotificationWorker>());

        services.AddHostedService<PlanetMessageWorker>();
        services.AddHostedService<StatWorker>();
        services.AddHostedService<ChannelWatchingWorker>();
        services.AddHostedService<UserOnlineWorker>();
        services.AddHostedService<NodeStateWorker>();
        services.AddHostedService<SubscriptionWorker>();
        services.AddHostedService<MigrationWorker>();
        services.AddHostedService<BlazorCssBundleService>();

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
