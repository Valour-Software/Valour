using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.OpenApi.Models;
using System.Net;
using System.Text.Json;
using Valour.Server.API;
using Valour.Server.Cdn;
using Valour.Server.Cdn.Api;
using Valour.Server.Cdn.Extensions;
using Valour.Server.Config;
using Valour.Server.Database;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Channels.Users;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Server.Email;
using Valour.Server.Workers;
using WebPush;

namespace Valour.Server
{
    public class Program
    {
        public static List<object> ItemApis { get; set; }

        public static NodeAPI NodeAPI { get; set; }

        public static async Task Main(string[] args)
        {
            // Create builder
            var builder = WebApplication.CreateBuilder(args);

            // Load configs
            LoadConfigsAsync(builder);

            // Initialize Email Manager
            EmailManager.SetupClient();

            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                options.Listen(IPAddress.Any, 5000, listenOptions =>
                {
                    listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3;
                });
            });

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

            // Notification routes
            NotificationsAPI.AddRoutes(app);

            // s3 (r2) setup
            BasicAWSCredentials cred = new(CdnConfig.Current.S3Access, CdnConfig.Current.S3Secret);
            AmazonS3Config config = new AmazonS3Config()
            {
                ServiceURL = CdnConfig.Current.S3Endpoint
            };

            AmazonS3Client client = new(cred, config);
            BucketManager.Client = client;

            ItemApis = new() {
                new ItemAPI<User>()                     .RegisterRoutes(app),
                new ItemAPI<Planet>()                   .RegisterRoutes(app),
                new ItemAPI<PlanetChatChannel>()        .RegisterRoutes(app),
                new ItemAPI<PlanetCategoryChannel>()    .RegisterRoutes(app),
                new ItemAPI<PlanetMember>()             .RegisterRoutes(app),
                new ItemAPI<PlanetRole>()               .RegisterRoutes(app),
                new ItemAPI<PlanetInvite>()             .RegisterRoutes(app),
                new ItemAPI<PlanetBan>()                .RegisterRoutes(app),
                new ItemAPI<PermissionsNode>()          .RegisterRoutes(app),
                new ItemAPI<UserFriend>()               .RegisterRoutes(app),
                new ItemAPI<DirectChatChannel>()        .RegisterRoutes(app)
            };

            NodeAPI = new NodeAPI(NodeConfig.Instance);
            NodeAPI.AddRoutes(app);

            // Migrations and tasks

            /*

            int c = 0;

            using (ValourDB db = new ValourDB(ValourDB.DBOptions)){
                foreach (var user in db.Users){
                    if (!db.PlanetMembers.Any(x => x.PlanetId == 735703679107072 &&
                         user.Id == x.UserId)){
                        
                        PlanetMember member = new(){
                            PlanetId = 735703679107072,
                            UserId = user.Id,
                            Id = IdManager.Generate(),
                            Nickname = user.Name
                        };

                        db.PlanetMembers.Add(member);

                        c++;
                     }
                }

                db.SaveChanges();

                Console.WriteLine($"Added {c} users to main planet.");
            }

            */

            // Run

            //using (ValourDB db = new(ValourDB.DBOptions))
            //{
            //    foreach (User user in await db.Users.ToListAsync())
            //    {
            //        if (user.Disabled)
            //        {
            //           await user.HardDelete(db);
            //        }
            //    }

            //    await db.SaveChangesAsync();
            //}

            app.Run();
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
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseSwagger();

            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
            });

            app.UseWebSockets();

            app.UseHttpsRedirection();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapRazorPages();
            app.MapControllers();
            app.MapFallbackToFile("_content/Valour.Client/index.html");
            app.MapHub<PlanetHub>(PlanetHub.HubUrl, options =>
            {
                options.LongPolling.PollTimeout = TimeSpan.FromSeconds(60);
            });

            //app.UseDeveloperExceptionPage();

            PlanetHub.Current = app.Services.GetService<IHubContext<PlanetHub>>();
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
                        "http://www.valour.gg",
                        "https://valour.gg",
                        "http://valour.gg",
                        "https://api.valour.gg",
                        "http://api.valour.gg",
                        "http://localhost:3000",
                        "https://localhost:3000",
                        "http://localhost:3001",
                        "https://localhost:3001");
                });
            });

            services.AddSignalR(options =>
            {
                options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            });

            services.AddHttpClient();

            services.Configure<FormOptions>(options =>
            {
                options.MemoryBufferThreshold = 10240000;
                options.MultipartBodyLengthLimit = 10240000;
            });

            services.AddDbContextPool<CdnDb>(options =>
            {
                options.UseNpgsql(CdnDb.ConnectionString);
            });

            services.AddDbContextPool<ValourDB>(options =>
            {
                options.UseNpgsql(ValourDB.ConnectionString);
            });

            // This probably needs to be customized further but the documentation changed
            services.AddAuthentication().AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

            // Adds user manager to dependency injection
            services.AddSingleton<WebPushClient>();
            services.AddControllersWithViews().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                //options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;

                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

            services.AddRazorPages();

            services.AddSingleton<CdnMemoryCache>();

            services.AddHostedService<MessageCacheWorker>();
            services.AddHostedService<PlanetMessageWorker>();
            services.AddHostedService<StatWorker>();
            services.AddHostedService<ChannelWatchingWorker>();

            services.AddEndpointsApiExplorer();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Valour API", Description = "The official Valour API", Version = "v1.0" });
                c.AddSecurityDefinition("Token", new OpenApiSecurityScheme()
                {
                    Description = "The token used for authorizing your account.",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Token"
                });
                c.OperationFilter<FileUploadOperation>();
            });
        }

        /// <summary>
        /// Loads the json configs for services
        /// </summary>
        public static void LoadConfigsAsync(WebApplicationBuilder builder)
        {
            var env = builder.Environment;
            var sharedFolder = Path.Combine(env.ContentRootPath, "..", "Config");
            builder.Configuration.AddJsonFile(Path.Combine(sharedFolder, "sharedSettings.json"), optional: false)
                                 .AddJsonFile("appsettings.json", optional: true)
                                 .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            builder.Configuration.GetSection("CDN").Get<CdnConfig>();
            builder.Configuration.GetSection("Database").Get<DbConfig>();
            builder.Configuration.GetSection("Email").Get<EmailConfig>();
            builder.Configuration.GetSection("Vapid").Get<VapidConfig>();
            builder.Configuration.GetSection("Node").Get<NodeConfig>();
        }
    }
}
