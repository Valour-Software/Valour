using NUglify;
using NUglify.Css;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Valour.Server.Utilities;

namespace Valour.Server.Services;

public class BlazorCssBundleService : IHostedService
{
    private static readonly string[] StaticCssFiles = new[]
    {
        // order matters!
        "_content/Valour.Client/css/reboot.min.css",
        "_content/Valour.Client/css/app.css",
        "_content/Valour.Client/css/globals.css",
    };
    
    private static readonly CssSettings CssSettings = new()
    {
        CommentMode = CssComment.None,
        OutputMode = OutputMode.SingleLine,
        ColorNames = CssColor.Hex,
        Indent = string.Empty,
        TermSemicolons = true,
        RemoveEmptyBlocks = true,
        DecodeEscapes = true,
        MinifyExpressions = true,
    };

    private readonly ILogger<BlazorCssBundleService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServer _server;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IWebHostEnvironment _environment;
    private readonly CancellationTokenSource _stoppingCts = new();

    public BlazorCssBundleService(
        ILogger<BlazorCssBundleService> logger,
        IHttpClientFactory httpClientFactory,
        IServer server,
        IConfiguration configuration,
        IHostApplicationLifetime applicationLifetime, 
        IWebHostEnvironment environment)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _server = server;
        _configuration = configuration;
        _applicationLifetime = applicationLifetime;
        _environment = environment;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = ExecuteAsync(_stoppingCts.Token);
        return Task.CompletedTask;
    }
    
    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        StartupWaitFlag.Increment(); // Hold requests until we're ready
        
        try
        {
            // Wait for the app to be fully started
            await WaitForApplicationStart(stoppingToken);

            _logger.LogInformation("Starting CSS bundle generation...");

            // Add a small delay to ensure everything is ready
            await Task.Delay(1000, stoppingToken);

            var baseUrl = GetServerAddress();
            using var client = _httpClientFactory.CreateClient();

            await GenerateBundle(baseUrl, client, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while generating CSS bundle");
        }
        
        StartupWaitFlag.Decrement(); // Allow requests to continue
    }

    private async Task WaitForApplicationStart(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        
        await using var registration = _applicationLifetime.ApplicationStarted.Register(() => tcs.TrySetResult());
        
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await tcs.Task;
        
        _logger.LogInformation("Application started, proceeding with CSS bundle generation");
    }

    private string GetServerAddress()
    {
        // First try to get from server addresses
        var addressFeature = _server.Features.Get<IServerAddressesFeature>();
        var address = addressFeature?.Addresses.FirstOrDefault();
        if (!string.IsNullOrEmpty(address))
        {
            return address;
        }

        // Fallback to configuration
        var host = _configuration["ServerHost"] ?? "localhost";
        var port = _configuration["ServerPort"] ?? "5000";
        var scheme = _configuration["ServerScheme"] ?? "http";

        return $"{scheme}://{host}:{port}";
    }

    private async Task GenerateBundle(string baseUrl, HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(
            $"{baseUrl}/Valour.Client.Blazor.styles.css", 
            cancellationToken
        );
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to fetch main CSS file: {response.StatusCode}");
        }

        var mainCss = await response.Content.ReadAsStringAsync(cancellationToken);
        
        // Extract imports
        var importRegex = new Regex(@"@import\s+['""](.*?)['""]\s*;");
        var imports = importRegex.Matches(mainCss)
            .Select(m => m.Groups[1].Value)
            .ToList();

        var toBundle = new List<string>();
        toBundle.AddRange(StaticCssFiles);
        toBundle.AddRange(imports);

        _logger.LogInformation("Found {Count} CSS imports to process", toBundle);

        // Combine all CSS
        var combinedCss = new StringBuilder();
        
        foreach (var importPath in toBundle)
        {
            var fullUrl = $"{baseUrl}/{importPath.TrimStart('/')}";
            _logger.LogInformation("Fetching: {Url}", fullUrl);
            
            var importResponse = await client.GetAsync(fullUrl, cancellationToken);
            if (!importResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch import {Path}: {Status}", 
                    importPath, importResponse.StatusCode);
                continue;
            }

            var buffer = await importResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            
            // Only append printable ASCII characters (0x20 to 0x7E)
            for (var i = 0; i < buffer.Length; i++)
            {
                var b = buffer[i];
                if (b >= 0x20 && b <= 0x7E)
                {
                    combinedCss.Append((char)b);
                }
            }
            
            combinedCss.AppendLine(); // Add separator between files
        }

        // Minify the combined CSS
        var minified = Uglify.Css(combinedCss.ToString(), CssSettings);
        
        if (minified.HasErrors)
        {
            // Log all errors
            foreach (var error in minified.Errors)
            {
                _logger.LogError("CSS minification error: {Error}", error);
            }
        }

        // Write to file
        var wwwrootPath = GetWwwRootPath();
        var cssPath = Path.Combine(wwwrootPath, "css");
        
        // Ensure directory exists
        Directory.CreateDirectory(cssPath);

        var outputPath = Path.Combine(cssPath, "bundled.min.css");

        _logger.LogInformation("Writing bundle to: {Path}", outputPath);
        await File.WriteAllTextAsync(
            outputPath, 
            minified.Code, 
            Encoding.UTF8,
            cancellationToken
        );

        _logger.LogInformation(
            "Successfully generated bundled CSS at: {Path}", 
            outputPath
        );
    }
    
    private string GetWwwRootPath()
    {
        // First try the content root path
        var contentRootPath = Directory.GetCurrentDirectory();
        
        // Look for the wwwroot in the published output
        var publishedWwwroot = Path.Combine(contentRootPath, "wwwroot");
        
        if (Directory.Exists(publishedWwwroot))
        {
            return publishedWwwroot;
        }

        // If not found, check if we're in development and need to find the Client project
        var directory = new DirectoryInfo(contentRootPath);
        
        // Go up until we find the solution directory
        while (directory != null && !directory.GetFiles("*.sln").Any())
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException("Could not find solution directory");
        }

        // Look for the Client project's wwwroot
        var dirs = Directory.GetDirectories(directory.FullName, "wwwroot",
            SearchOption.AllDirectories);
        
         // Get main client project
        var clientWwwroot = dirs.FirstOrDefault(d => d.Contains("Client" + Path.DirectorySeparatorChar));

        if (clientWwwroot == null)
        {
            throw new DirectoryNotFoundException(
                "Could not find Client project wwwroot directory");
        }

        return clientWwwroot;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
