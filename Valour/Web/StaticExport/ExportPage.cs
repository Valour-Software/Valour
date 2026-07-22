namespace Valour.Web.StaticExport;

public sealed record ExportPage(
    string Controller,
    string Action,
    string RequestPath,
    string OutputPath);
