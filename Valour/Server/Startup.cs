using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Pomelo.EntityFrameworkCore.MySql.Storage;
using System;

namespace Valour.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            DBConfig dbConfig = null;

            string dbConfigPath = Directory.GetCurrentDirectory() + "/DBConfig.json";

            if (File.Exists(dbConfigPath))
            {
                string data = File.ReadAllText(dbConfigPath);
                dbConfig = JsonConvert.DeserializeObject<DBConfig>(data);
            }
            else
            {
                dbConfig = new DBConfig()
                {
                    Host = "INSERT HOST",
                    Password = "INSERT PASSWORD",
                    Username = "INSERT USERNAME"
                };

                string data = JsonConvert.SerializeObject(dbConfig);

                File.WriteAllText(dbConfigPath, data);
            }

            DBConfig.instance = dbConfig;

            services.AddDbContextPool<ValourDB>(options =>
            {
                options.UseMySql(ValourDB.ConnectionString, options => options.EnableRetryOnFailure().CharSet(CharSet.Utf8Mb4).ServerVersion(new Version(8, 0, 20), ServerType.MySql));
            });

            services.AddControllersWithViews();
            services.AddRazorPages();
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

            app.UseHttpsRedirection();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}
