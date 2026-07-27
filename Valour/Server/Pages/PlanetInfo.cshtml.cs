#nullable enable annotations

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Valour.Server.Services;
using Valour.Shared.Models;
using Valour.Server.Models;

namespace Valour.Server.Pages;

public class PlanetInfoModel : PageModel
{
    private readonly PlanetService _planetService;
    private readonly ITagService _tagService;

    public PlanetInfoModel(PlanetService planetService, ITagService tagService)
    {
        _planetService = planetService;
        _tagService = tagService;
    }

    [BindProperty(SupportsGet = true)]
    public string PlanetIdText { get; set; } = string.Empty;

    public PlanetListInfo? PlanetInfo { get; set; }
    public List<ISharedPlanetTag> Tags { get; set; } = new();
    public int TagCount => Tags.Count;
    public string? ErrorMessage { get; set; }
    public string RequestUrl => $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";

    /// <summary>
    /// One canonical per planet regardless of whether the id or vanity form
    /// was requested, so the two never compete as duplicate content
    /// </summary>
    public string CanonicalUrl => PlanetInfo is null
        ? RequestUrl
        : $"{Request.Scheme}://{Request.Host}/p/{PlanetInfo.PlanetId}";

    /// <summary>
    /// Organization structured data — serialized so user-authored name and
    /// description are safely escaped inside the inline script block
    /// </summary>
    public string JsonLd
    {
        get
        {
            if (PlanetInfo is null)
                return "{}";

            var data = new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "Organization",
                ["name"] = PlanetInfo.Name,
                ["description"] = PlanetInfo.Description,
                ["url"] = CanonicalUrl,
                ["logo"] = ISharedPlanet.GetIconUrl(PlanetInfo, IconFormat.Webp256),
                ["memberOf"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Organization",
                    ["name"] = "Valour",
                    ["url"] = "https://valour.gg",
                },
            };

            return System.Text.Json.JsonSerializer.Serialize(data);
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Vanity names identify planets in public URLs too
        if (!long.TryParse(PlanetIdText, out var planetId))
        {
            planetId = await _planetService.ResolveVanityAsync(PlanetIdText) ?? 0;
        }

        if (planetId == 0)
        {
            ErrorMessage = "Invalid planet ID format.";
            return Page();
        }

        if (planetId <= 0)
        {
            ErrorMessage = "Invalid planet ID.";
            return Page();
        }

        try
        {
            PlanetInfo = await _planetService.GetPlanetInfoAsync(planetId);
            if (PlanetInfo == null)
            {
                ErrorMessage = "The planet you're looking for doesn't exist or is not public.";
                Response.StatusCode = 404;
                return Page();
            }

            // Tags are now included in the PlanetInfo from the server
            if (PlanetInfo.Tags != null && PlanetInfo.Tags.Count > 0)
            {
                Tags = PlanetInfo.Tags.Take(10).Cast<ISharedPlanetTag>().ToList();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "Failed to load planet information. Please try again later.";
            Console.WriteLine($"Error loading planet {planetId}: {ex.Message}");
        }

        return Page();
    }
}
