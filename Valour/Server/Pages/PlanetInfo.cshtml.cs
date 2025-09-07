#nullable enable

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

    public async Task<IActionResult> OnGetAsync()
    {
        if (!long.TryParse(PlanetIdText, out var planetId))
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
