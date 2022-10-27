using System.Net;

var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddCors(options =>
//{
//    options.AddDefaultPolicy(
//                      builder => builder.WithOrigins("https://app.valour.gg")
//                                        .AllowAnyMethod()
//                                        .AllowAnyHeader());
//});

var env = builder.Environment;
var sharedFolder = Path.Combine(env.ContentRootPath, "..", "Config");
builder.Configuration.AddJsonFile(Path.Combine(sharedFolder, "sharedSettings.json"), optional: false)
                     .AddJsonFile("appsettings.json", optional: true)
                     .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

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

