using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Text.Json;
using System.IO;
using Valour.Server.Database;
using Valour.Server.Users.Identity;
using Valour.Server.Email;
using AutoMapper;
using Valour.Server.Mapping;
using Valour.Server.Workers;
using Valour.Server.Planets;
using Valour.Server.Roles;
using Valour.Server.Notifications;
using WebPush;
using Valour.Server.MPS;
using System.Text.Json.Serialization;
using Valour.Server.API;
using System.Web;
using Microsoft.OpenApi.Models;
using System.Net;

namespace Valour.Server
{
    public class Program
    {
        public const string CONF_LOC = "ValourConfig/";
        public const string DBCONF_FILE = "DBConfig.json";
        public const string EMCONF_FILE = "EmailConfig.json";
        public const string MPSCONF_FILE = "MPSConfig.json";
        public const string VAPIDCONF_FILE = "VapidConfig.json";

        public static void Main(string[] args)
        {
            // Load configs
            LoadConfigsAsync();

            // Create builder
            var builder = WebApplication.CreateBuilder(args);

            builder.WebHost.ConfigureKestrel((context, options) =>
            {
                options.Listen(IPAddress.Any, 3001, listenOptions =>
                {
                    listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3;
                    listenOptions.UseHttps();
                });
            });

            builder.Host.ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls("http://localhost:3000", "https://localhost:3001");
            });

            // Set up services
            ConfigureServices(builder);

            // Build web app
            var app = builder.Build();

            // Configure application
            ConfigureApp(app);

            app.MapGet("/api/ping", () => "pong");

            // Add API routes
            BaseAPI       .AddRoutes(app);
            UploadAPI     .AddRoutes(app);
            ChannelAPI    .AddRoutes(app);
            CategoryAPI   .AddRoutes(app);
            PlanetAPI     .AddRoutes(app);
            UserAPI       .AddRoutes(app);
            MemberAPI     .AddRoutes(app);
            RoleAPI       .AddRoutes(app);
            EmbedAPI      .AddRoutes(app);

            // Run
            app.Run();
        }

        public static void ConfigureApp(WebApplication app)
        {
            app.UseCors("AllowAll");

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
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

            app.UseEndpoints(endpoints =>
            {
                //endpoints.MapBlazorHub();
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapFallbackToFile("index.html");
                endpoints.MapHub<PlanetHub>(PlanetHub.HubUrl, options =>
                {
                    //options.LongPolling.PollTimeout = TimeSpan.FromSeconds(60);
                });
            });

            //app.UseDeveloperExceptionPage();

            PlanetHub.Current = app.Services.GetService<IHubContext<PlanetHub>>();

            /* Reference code for any future migrations */

            using ValourDB db = new(ValourDB.DBOptions);
            db.Database.EnsureCreated();

            foreach (ServerPlanetRole role in db.PlanetRoles.Include(x => x.Planet))
            {
                if (role.Id == role.Planet.Default_Role_Id)
                {
                    role.Position = uint.MaxValue;
                }
            }

            db.SaveChanges();
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

            services.AddSignalR();

            var mapConfig = new MapperConfiguration(x =>
            {
                x.AddProfile(new MappingProfile());
            });

            IMapper mapper = mapConfig.CreateMapper();

            services.AddSingleton(mapper);

            services.AddHttpClient();

            MappingManager.Mapper = mapper;

            services.AddDbContextPool<ValourDB>(options =>
            {
                options.UseMySql(ValourDB.ConnectionString, ServerVersion.Parse("8.0.20-mysql"), options => options.EnableRetryOnFailure());
            });

            // This probably needs to be customized further but the documentation changed
            services.AddAuthentication().AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

            // Adds user manager to dependency injection
            services.AddScoped<UserManager>();
            IdManager idManager = new();
            services.AddSingleton(idManager);
            services.AddSingleton<WebPushClient>();
            services.AddControllersWithViews().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                //options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;

                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            }
            );
            services.AddRazorPages();
            services.AddHostedService<MessageCacheWorker>();
            services.AddHostedService<PlanetMessageWorker>();
            services.AddHostedService<StatWorker>();

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
            });
        }

        /// <summary>
        /// Loads the json configs for services
        /// </summary>
        public static async Task LoadConfigsAsync()
        {
            // Create directory if it doesn't exist
            if (!Directory.Exists(CONF_LOC))
            {
                Directory.CreateDirectory(CONF_LOC);
            }

            // Load database settings
            DBConfig dbconfig;
            if (File.Exists(CONF_LOC + DBCONF_FILE))
            {
                // If there is a config, read it
                dbconfig = await JsonSerializer.DeserializeAsync<DBConfig>(File.OpenRead(CONF_LOC + DBCONF_FILE));
            }
            else
            {
                // Otherwise create a config with default values and write it to the location
                dbconfig = new DBConfig()
                {
                    Database = "database",
                    Host = "host",
                    Password = "password",
                    Username = "user"
                };

                File.WriteAllText(CONF_LOC + DBCONF_FILE, JsonSerializer.Serialize(dbconfig));
                Console.WriteLine("Error: No DB config was found. Creating file...");
            }

            EmailConfig emconfig;
            if (File.Exists(CONF_LOC + EMCONF_FILE))
            {
                // If there is a config, read it
                emconfig = await JsonSerializer.DeserializeAsync<EmailConfig>(File.OpenRead(CONF_LOC + EMCONF_FILE));
            }
            else
            {
                // Otherwise create a config with default values and write it to the location
                emconfig = new EmailConfig()
                {
                    Api_Key = "api_key_goes_here"
                };

                File.WriteAllText(CONF_LOC + EMCONF_FILE, JsonSerializer.Serialize(emconfig));
                Console.WriteLine("Error: No Email config was found. Creating file...");
            }

            // Initialize Email Manager
            EmailManager.SetupClient();

            MPSConfig vmpsconfig;
            if (File.Exists(CONF_LOC + MPSCONF_FILE))
            {
                // If there is a config, read it
                vmpsconfig = await JsonSerializer.DeserializeAsync<MPSConfig>(File.OpenRead(CONF_LOC + MPSCONF_FILE));
            }
            else
            {
                // Otherwise create a config with default values and write it to the location
                vmpsconfig = new MPSConfig()
                {
                    Api_Key = "api_key_goes_here"
                };

                File.WriteAllText(CONF_LOC + MPSCONF_FILE, JsonSerializer.Serialize(vmpsconfig));
                Console.WriteLine("Error: No MSP config was found. Creating file...");
            }

            vmpsconfig.Api_Key_Encoded = HttpUtility.UrlEncode(vmpsconfig.Api_Key);

            VapidConfig vapidconfig;
            if (File.Exists(CONF_LOC + VAPIDCONF_FILE))
            {
                // If there is a config, read it
                vapidconfig = await JsonSerializer.DeserializeAsync<VapidConfig>(File.OpenRead(CONF_LOC + VAPIDCONF_FILE));
            }
            else
            {
                // Otherwise create a config with default values and write it to the location
                vapidconfig = new VapidConfig()
                {
                    Subject = "mailto: <>",
                    PublicKey = "public-key-here",
                    PrivateKey = "private-key-here"
                };

                File.WriteAllText(CONF_LOC + VAPIDCONF_FILE, JsonSerializer.Serialize(vapidconfig));
                Console.WriteLine("Error: No Vapid config was found. Creating file...");
            }
        }
    }
}
