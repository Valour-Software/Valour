using Valour.Web.StaticExport;

if (StaticExportOptions.IsExportRequested(args))
    Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<RazorViewRenderer>();
builder.Services.AddSingleton<StaticSiteExporter>();
//builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Any, 5000));

var app = builder.Build();

if (StaticExportOptions.TryCreate(args, app.Environment.ContentRootPath, out var exportOptions))
{
    await app.Services.GetRequiredService<StaticSiteExporter>().ExportAsync(exportOptions);
    return;
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
