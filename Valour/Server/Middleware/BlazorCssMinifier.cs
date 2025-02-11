using NUglify;
using NUglify.Css;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Valour.Server.Middleware;

public class BlazorCssMinifier
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BlazorCssMinifier> _logger;
    
    private static readonly CssSettings CssSettings = new()
    {
        CommentMode = CssComment.Important, // Keep important comments
        OutputMode = OutputMode.SingleLine,
        ColorNames = CssColor.Hex,
        Indent = string.Empty,
        TermSemicolons = true,
        RemoveEmptyBlocks = true,
        DecodeEscapes = true,
        MinifyExpressions = true,
    };

    public BlazorCssMinifier(
        RequestDelegate next, 
        IMemoryCache cache, 
        ILogger<BlazorCssMinifier> logger
    )
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;

        if (path != null && path.Contains(".bundle.scp.css"))
        {
            // Handle 304 Not Modified
            var etag = context.Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(etag) && _cache.TryGetValue($"etag:{path}", out string cachedEtag) 
                && etag == cachedEtag)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            if (_cache.TryGetValue(path, out string cachedCss))
            {
                await SendCssResponse(context, cachedCss);
                return;
            }

            var originalBody = context.Response.Body;
            using var memoryStream = new MemoryStream();
            context.Response.Body = memoryStream;

            await _next(context);

            if (context.Response.StatusCode != StatusCodes.Status200OK)
            {
                context.Response.Body = originalBody;
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalBody);
                return;
            }

            memoryStream.Position = 0;
            var buffer = new byte[memoryStream.Length];
            _ = await memoryStream.ReadAsync(buffer);

            // Create a StringBuilder with initial capacity
            var sb = new StringBuilder(buffer.Length);

            // Only append printable ASCII characters (0x20 to 0x7E)
            for (var i = 0; i < buffer.Length; i++)
            {
                var b = buffer[i];
                if (b >= 0x20 && b <= 0x7E)
                {
                    sb.Append((char)b);
                }
            }

            var cleanCss = sb.ToString();

            _logger.LogInformation("Original CSS size: {Size} bytes", cleanCss.Length);

            try
            {
                var resultCss = Uglify.Css(cleanCss, CssSettings).Code;
                var reduction = ((1 - (double)resultCss.Length / cleanCss.Length) * 100);
                _logger.LogInformation(
                    "Minified CSS size: {Size} bytes ({Reduction:F2}% reduction)",
                    resultCss.Length,
                    reduction);

                // Generate ETag
                var newEtag = $"\"{Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(resultCss)))}\"";
                
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromHours(24));
                _cache.Set($"etag:{path}", newEtag, cacheOptions);
                _cache.Set(path, resultCss, cacheOptions);

                context.Response.Body = originalBody;
                await SendCssResponse(context, resultCss, newEtag);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CSS file");
                context.Response.Body = originalBody;
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalBody);
            }
        }
        else
        {
            await _next(context);
        }
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

// Extension method to make it easier to add the middleware
public static class BlazorCssMinifierMiddlewareExtensions
{
    public static IApplicationBuilder UseBlazorCssMinifier(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<BlazorCssMinifier>();
    }
}
