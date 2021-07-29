using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using System;
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

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public const string CONF_LOC = "ValourConfig/";
        public const string DBCONF_FILE = "DBConfig.json";
        public const string EMCONF_FILE = "EmailConfig.json";
        public const string MPSCONF_FILE = "MPSConfig.json";
        public const string VAPIDCONF_FILE = "VapidConfig.json";

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
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

            LoadConfigs();

            services.AddSignalR();

            var mapConfig = new MapperConfiguration(x =>
            {
                x.AddProfile(new MappingProfile());
            });

            IMapper mapper = mapConfig.CreateMapper();

            services.AddSingleton(mapper);

            MappingManager.Mapper = mapper;

            services.AddDbContextPool<ValourDB>(options =>
            {
                options.UseMySql(ValourDB.ConnectionString, ServerVersion.Parse("8.0.20-mysql"), options => options.EnableRetryOnFailure());
            });

            // This probably needs to be customized further but the documentation changed
            services.AddAuthentication().AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

            // Adds user manager to dependency injection
            services.AddScoped<UserManager>();
            IdManager idManager = new IdManager();
            services.AddSingleton<IdManager>(idManager);
            services.AddSingleton<WebPushClient>();
            services.AddControllersWithViews().AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
                }
            );
            services.AddRazorPages();
            services.AddHostedService<MessageCacheWorker>();
            services.AddHostedService<PlanetMessageWorker>();
            services.AddHostedService<StatWorker>();
        }

        /// <summary>
        /// Loads the json configs for services
        /// </summary>
        public void LoadConfigs()
        {
            // Load database settings
            DBConfig dbconfig = null;

            // Create directory if it doesn't exist
            if (!Directory.Exists(CONF_LOC))
            {
                Directory.CreateDirectory(CONF_LOC);
            }

            if (File.Exists(CONF_LOC + DBCONF_FILE))
            {
                // If there is a config, read it
                dbconfig = JsonConvert.DeserializeObject<DBConfig>(File.ReadAllText(CONF_LOC + DBCONF_FILE));
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

                File.WriteAllText(CONF_LOC + DBCONF_FILE, JsonConvert.SerializeObject(dbconfig));
                Console.WriteLine("Error: No DB config was found. Creating file...");
            }

            EmailConfig emconfig = null;

            if (File.Exists(CONF_LOC + EMCONF_FILE))
            {
                // If there is a config, read it
                emconfig = JsonConvert.DeserializeObject<EmailConfig>(File.ReadAllText(CONF_LOC + EMCONF_FILE));
            }
            else
            {
                // Otherwise create a config with default values and write it to the location
                emconfig = new EmailConfig()
                {
                    Api_Key = "api_key_goes_here"
                };

                File.WriteAllText(CONF_LOC + EMCONF_FILE, JsonConvert.SerializeObject(emconfig));
                Console.WriteLine("Error: No Email config was found. Creating file...");
            }

            // Initialize Email Manager
            EmailManager.SetupClient();

            MPSConfig mspconfig = null;

            if (File.Exists(CONF_LOC + MPSCONF_FILE))
            {
                // If there is a config, read it
                mspconfig = JsonConvert.DeserializeObject<MPSConfig>(File.ReadAllText(CONF_LOC + MPSCONF_FILE));
            }
            else
            {
                // Otherwise create a config with default values and write it to the location
                mspconfig = new MPSConfig()
                {
                    Api_Key = "api_key_goes_here"
                };

                File.WriteAllText(CONF_LOC + MPSCONF_FILE, JsonConvert.SerializeObject(mspconfig));
                Console.WriteLine("Error: No MSP config was found. Creating file...");
            }

            VapidConfig vapidconfig = null;

            if (File.Exists(CONF_LOC + VAPIDCONF_FILE))
            {
                // If there is a config, read it
                vapidconfig = JsonConvert.DeserializeObject<VapidConfig>(File.ReadAllText(CONF_LOC + VAPIDCONF_FILE));
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

                File.WriteAllText(CONF_LOC + VAPIDCONF_FILE, JsonConvert.SerializeObject(vapidconfig));
                Console.WriteLine("Error: No Vapid config was found. Creating file...");
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseCors("AllowAll");

            if (env.IsDevelopment())
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

            PlanetHub.Current = app.ApplicationServices.GetService<IHubContext<PlanetHub>>();

            /* Reference code for any future migrations */
            
            using (ValourDB db = new ValourDB(ValourDB.DBOptions))
            {
                foreach (ServerPlanetRole role in db.PlanetRoles.Include(x => x.Planet))
                {
                    if (role.Id == role.Planet.Default_Role_Id)
                    {
                        role.Position = uint.MaxValue;
                    }
                }

                db.SaveChanges();
            }
            
        }
    }
}
