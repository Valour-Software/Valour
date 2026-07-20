#nullable enable annotations

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Valour.Client.Components.Wiki.Display;
using Valour.Config.Configs;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;
using Valour.Shared.Models.Wiki;

namespace Valour.Server.Pages;

public class PlanetWikiModel : PageModel
{
    private readonly ValourDb _db;
    private readonly PlanetWikiService _wikiService;

    public PlanetWikiModel(ValourDb db, PlanetWikiService wikiService)
    {
        _db = db;
        _wikiService = wikiService;
    }

    [BindProperty(SupportsGet = true)]
    public string? PlanetIdOrVanity { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    public Valour.Database.Planet? Planet { get; set; }
    public bool IsPrivate { get; set; }
    public string? ErrorMessage { get; set; }
    public string PlanetIcon { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;

    public List<Models.PlanetWikiPage> Docs { get; } = new();
    public List<WikiTreeNodeData> Tree { get; set; } = new();
    public List<Models.PlanetWikiPage> RootNodes { get; } = new();
    public List<WikiSearchResult>? SearchResults { get; set; }

    public string CanonicalUrl { get; set; } = string.Empty;
    public string AppLink { get; set; } = string.Empty;

    public string GetPageUrl(string slug) => PublicWikiPageHelpers.GetPageUrl(Planet!, slug);

    public async Task<IActionResult> OnGetAsync()
    {
        Planet = await PublicWikiPageHelpers.ResolvePlanetAsync(_db, PlanetIdOrVanity);
        if (Planet is null)
        {
            Response.StatusCode = 404;
            ErrorMessage = "This wiki doesn't exist.";
            return Page();
        }

        PlanetIcon = PublicThreadPageHelpers.NormalizeMediaUrl(ISharedPlanet.GetIconUrl(Planet, IconFormat.Webp128));
        AppLink = $"{HostingConfig.Current.AppBaseUrl}/planetwiki/{Planet.Id}";
        CanonicalUrl = PublicWikiPageHelpers.GetHomeUrl(Planet);

        if (!Planet.EnableWiki || !Planet.PublicWiki)
        {
            IsPrivate = true;
            return Page();
        }

        // Canonicalize id-form URLs to the vanity form when one is claimed
        if (!string.IsNullOrWhiteSpace(Planet.Vanity) &&
            long.TryParse(PlanetIdOrVanity, out _))
        {
            var target = string.IsNullOrWhiteSpace(Query)
                ? CanonicalUrl
                : $"{CanonicalUrl}?q={Uri.EscapeDataString(Query)}";
            return RedirectPermanent(target);
        }

        Snippet = string.IsNullOrWhiteSpace(Planet.Description)
            ? $"The community wiki for {Planet.Name} on Valour."
            : PublicWikiPageHelpers.ToPlainSnippet(Planet.Description);

        Docs.AddRange(await _wikiService.GetTreeAsync(Planet.Id, publishedOnly: true));
        Tree = PublicWikiPageHelpers.BuildTree(Docs, Planet, activeSlug: null);
        RootNodes.AddRange(Docs.Where(x => x.ParentId is null).OrderBy(x => x.Position));

        if (!string.IsNullOrWhiteSpace(Query))
            SearchResults = await _wikiService.SearchAsync(Planet.Id, Query);

        Response.Headers.CacheControl = "public, max-age=60";
        return Page();
    }

}
