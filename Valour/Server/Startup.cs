using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Pomelo.EntityFrameworkCore.MySql.Storage;
using System;
using System.IO;
using System.Linq;
using Valour.Server.Database;
using Valour.Server.Messages;
using Microsoft.AspNetCore.Http;
using Valour.Server.Users.Identity;
using Valour.Server.Users;
using Valour.Server.Email;
using AutoMapper;
using Valour.Server.Mapping;
using Valour.Server.Workers;
using Valour.Server.MSP;
using Valour.Server.Planets;
using Microsoft.Net.Http.Headers;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
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
        public const string MSPCONF_FILE = "MSPConfig.json";

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            LoadConfigs();

            services.AddSignalR();

            var mapConfig = new MapperConfiguration(x =>
            {
                x.AddProfile(new MappingProfile());
            });

            IMapper mapper = mapConfig.CreateMapper();

            services.AddSingleton(mapper);

            services.AddDbContextPool<ValourDB>(options =>
            {
                options.UseMySql(ValourDB.ConnectionString, ServerVersion.FromString("8.0.20-mysql"), options => options.EnableRetryOnFailure().CharSet(CharSet.Utf8Mb4));
            });

            // This probably needs to be customized further but the documentation changed
            services.AddAuthentication().AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

            // Adds user manager to dependency injection
            services.AddScoped<UserManager>();
            services.AddControllersWithViews();
            services.AddRazorPages();
            services.AddHostedService<MessageCacheWorker>();
            services.AddHostedService<PlanetMessageWorker>();
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

            MSPConfig mspconfig = null;

            if (File.Exists(CONF_LOC + MSPCONF_FILE))
            {
                // If there is a config, read it
                mspconfig = JsonConvert.DeserializeObject<MSPConfig>(File.ReadAllText(CONF_LOC + MSPCONF_FILE));
            }
            else
            {
                // Otherwise create a config with default values and write it to the location
                mspconfig = new MSPConfig()
                {
                    Api_Key = "api_key_goes_here"
                };

                File.WriteAllText(CONF_LOC + MSPCONF_FILE, JsonConvert.SerializeObject(mspconfig));
                Console.WriteLine("Error: No MSP config was found. Creating file...");
            }
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
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
       
            app.UseStaticFiles(new StaticFileOptions { 
                OnPrepareResponse = x =>
                {
                    x.Context.Response.Headers[HeaderNames.CacheControl] = "no-store";
                }
            });

            app.UseRouting();

            app.UseAuthorization();
            app.UseAuthentication();

            app.UseEndpoints(endpoints =>
            {
                //endpoints.MapBlazorHub();
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapFallbackToFile("index.html");
                endpoints.MapHub<PlanetHub>(PlanetHub.HubUrl, options =>
                {
                    options.LongPolling.PollTimeout = TimeSpan.FromSeconds(60);
                }); 
            });

            PlanetHub.Current = app.ApplicationServices.GetService<IHubContext<PlanetHub>>();

            using (var serviceScope = app.ApplicationServices.GetService<IServiceScopeFactory>().CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetRequiredService<ValourDB>();
                context.Database.EnsureCreated();
            }
        }
    }
}
