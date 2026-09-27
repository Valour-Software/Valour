namespace Valour.Web.StaticExport;

/// <summary>
/// A page the static export renders. Pages with <paramref name="InSitemap"/> false,
/// such as utility pages, are still published but left out of sitemap.xml.
/// </summary>
public sealed record ExportPage(
    string Controller,
    string Action,
    string RequestPath,
    string OutputPath,
    bool InSitemap = true);
