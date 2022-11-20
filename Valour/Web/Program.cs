using Microsoft.EntityFrameworkCore;
using System.Net;
using Valour.Server.Config;
using Valour.Server.Database;
using Valour.Web.Controllers;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.GetSection("CDN").Get<CdnConfig>();
builder.Configuration.GetSection("Database").Get<DbConfig>();
builder.Configuration.GetSection("Email").Get<EmailConfig>();
builder.Configuration.GetSection("Vapid").Get<VapidConfig>();
builder.Configuration.GetSection("Node").Get<NodeConfig>();

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.Listen(IPAddress.Any, 3000, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3;
        //listenOptions.UseHttps();
    });
});

builder.Services.AddDbContextPool<ValourDB>(options =>
{
    options.UseNpgsql(ValourDB.ConnectionString);
});

var app = builder.Build();

//app.UseCors();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

await Valour.Web.MarkdownToHtml.LoadMarkdown();
app.Run();

