using System.Text;

namespace Valour.Server.Middleware;

public class BlazorCssInjector
{
    private readonly RequestDelegate _next;
    
    public BlazorCssInjector(RequestDelegate next)
    {
        _next = next;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        
        if (path is not null && path.Contains("bundled.min.css"))
        {
            // Return cached CSS if available
            if (!string.IsNullOrWhiteSpace(BlazorCssBundleService.GeneratedCss))
            {
                await SendCssResponse(context, BlazorCssBundleService.GeneratedCss);
                return;
            }
        }
        
        await _next(context);
    }
    
    private async Task SendCssResponse(HttpContext context, string css, string etag = null)
    {
        context.Response.Clear();
        context.Response.ContentType = "text/css; charset=utf-8";
        
        if (etag != null)
        {
            context.Response.Headers.ETag = etag;
        }

        // Use UTF8 without BOM
        var bytes = new UTF8Encoding(false).GetBytes(css);
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes);
    }
}