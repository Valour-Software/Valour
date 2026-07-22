using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace Valour.Web.StaticExport;

public sealed class RazorViewRenderer
{
    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RazorViewRenderer> _logger;

    public RazorViewRenderer(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceScopeFactory scopeFactory,
        ILogger<RazorViewRenderer> logger)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string> RenderAsync(ExportPage page)
    {
        using var scope = _scopeFactory.CreateScope();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };

        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("valour.gg");
        httpContext.Request.Path = page.RequestPath;

        var routeData = new RouteData();
        routeData.Values["controller"] = page.Controller;
        routeData.Values["action"] = page.Action;

        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        _logger.LogInformation("Finding Razor view {Action}", page.Action);
        var viewResult = _viewEngine.FindView(actionContext, page.Action, isMainPage: true);
        if (!viewResult.Success)
        {
            var searched = string.Join(Environment.NewLine, viewResult.SearchedLocations ?? Array.Empty<string>());
            throw new InvalidOperationException($"Could not find view '{page.Action}' for static export.{Environment.NewLine}{searched}");
        }

        await using var writer = new StringWriter();
        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        var tempData = new TempDataDictionary(httpContext, _tempDataProvider);
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            tempData,
            writer,
            new HtmlHelperOptions());

        _logger.LogInformation("Rendering Razor view {Action}", page.Action);
        await viewResult.View.RenderAsync(viewContext);
        _logger.LogInformation("Rendered Razor view {Action}", page.Action);
        return writer.ToString();
    }
}
