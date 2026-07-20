#nullable enable annotations

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Valour.Client.Components.Wiki.Display;
using Valour.Config.Configs;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Server.Pages;

public class WikiPageViewModel : PageModel
{
    private readonly ValourDb _db;
    private readonly PlanetWikiService _wikiService;

    public WikiPageViewModel(ValourDb db, PlanetWikiService wikiService)
    {
        _db = db;
        _wikiService = wikiService;
    }

    [BindProperty(SupportsGet = true)]
    public string? PlanetIdOrVanity { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Slug { get; set; }

    public Valour.Database.Planet? Planet { get; set; }
    public Models.PlanetWikiPage? Doc { get; set; }

    public bool IsPrivate { get; set; }
    public string? ErrorMessage { get; set; }
    public string PlanetIcon { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public string? LastEditorName { get; set; }

    public RenderedDoc Rendered { get; set; } = new();
    public List<WikiTreeNodeData> Tree { get; set; } = new();
    public Models.PlanetWikiPage? PrevPage { get; set; }
    public Models.PlanetWikiPage? NextPage { get; set; }

    public string CanonicalUrl { get; set; } = string.Empty;
    public string HomeUrl { get; set; } = string.Empty;
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
        HomeUrl = PublicWikiPageHelpers.GetHomeUrl(Planet);
        AppLink = $"{HostingConfig.Current.AppBaseUrl}/planetwiki/{Planet.Id}";

        if (!Planet.EnableWiki || !Planet.PublicWiki)
        {
            IsPrivate = true;
            return Page();
        }

        var (doc, movedFrom) = await _wikiService.ResolveSlugAsync(Planet.Id, Slug ?? string.Empty);
        if (doc is null || doc.IsFolder || !doc.IsPublished || doc.Slug is null)
        {
            Response.StatusCode = 404;
            ErrorMessage = "This page doesn't exist or was removed.";
            return Page();
        }

        // Renamed slugs and id-form planet URLs both 301 to the canonical form
        CanonicalUrl = PublicWikiPageHelpers.GetPageUrl(Planet, doc.Slug);
        var usedIdForm = !string.IsNullOrWhiteSpace(Planet.Vanity) &&
                         long.TryParse(PlanetIdOrVanity, out _);
        if (movedFrom || usedIdForm)
            return RedirectPermanent(CanonicalUrl);

        Doc = doc;
        AppLink = $"{HostingConfig.Current.AppBaseUrl}/planetwiki/{Planet.Id}/{doc.Id}";

        var content = await _wikiService.GetContentAsync(Planet.Id, doc.Id);
        Rendered = PublicWikiPageHelpers.RenderDoc(content?.Content);
        Snippet = PublicWikiPageHelpers.ToPlainSnippet(content?.Content);

        var docs = await _wikiService.GetTreeAsync(Planet.Id, publishedOnly: true);
        Tree = PublicWikiPageHelpers.BuildTree(docs, Planet, doc.Slug);

        var pages = PublicWikiPageHelpers.FlattenPages(docs);
        var index = pages.FindIndex(x => x.Id == doc.Id);
        if (index > 0)
            PrevPage = pages[index - 1];
        if (index >= 0 && index < pages.Count - 1)
            NextPage = pages[index + 1];

        var editorId = doc.LastEditedByUserId ?? doc.CreatedByUserId;
        LastEditorName = await _db.Users.AsNoTracking()
            .Where(x => x.Id == editorId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync();

        Response.Headers.CacheControl = "public, max-age=60";
        return Page();
    }

}
