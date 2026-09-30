using System.Text.RegularExpressions;
using Valour.Config.Configs;

namespace Valour.Server.Email;

public static partial class EmailTemplateHelper
{
    /// <summary>
    /// Email clients cannot load the app's web fonts reliably, so the stack falls back
    /// to each platform's interface font.
    /// </summary>
    public const string FontStack =
        "\"Instrument Sans\", -apple-system, BlinkMacSystemFont, \"Segoe UI\", Roboto, Helvetica, Arial, sans-serif";

    public const string CanvasColor = "#0a0d12";
    public const string SurfaceColor = "#0e1218";
    public const string LineColor = "#252c38";
    public const string TextPrimaryColor = "#e7ebf1";
    public const string TextSecondaryColor = "#bcc3cd";
    public const string TextTertiaryColor = "#7d8696";
    public const string PrimaryColor = "#8fd3ff";
    public const string PrimaryContrastColor = "#0a0d12";

    public const string HeadingStyle =
        $"margin: 0 0 16px 0; color: {TextPrimaryColor}; font-size: 22px; font-weight: 600; line-height: 1.3;";

    public const string ParagraphStyle =
        $"margin: 0 0 14px 0; color: {TextSecondaryColor}; font-size: 15px; line-height: 1.6;";

    public const string ButtonStyle =
        $"display: inline-block; margin: 4px 0 18px 0; padding: 11px 20px; background-color: {PrimaryColor}; color: {PrimaryContrastColor}; font-size: 14px; font-weight: 600; text-decoration: none; border-radius: 7px;";

    public const string LinkStyle = $"color: {PrimaryColor}; word-break: break-all;";

    [GeneratedRegex("<a href='([^']*)'>")]
    private static partial Regex UnstyledLinkRegex();

    /// <summary>
    /// Wraps email body content in a proper HTML document structure with consistent styling.
    /// If unsubscribeUrl is provided (marketing emails), adds an unsubscribe link in the footer.
    /// Transactional emails (registration, password reset) should pass null for unsubscribeUrl.
    /// </summary>
    public static string WrapInTemplate(string bodyContent, string unsubscribeUrl = null)
    {
        var address = EmailConfig.Instance?.PhysicalAddress ?? "99 Wall Street Suite 1299, New York, NY";
        var logoUrl = EmailConfig.Instance?.LogoUrl ?? "https://valour.gg/media/logo/logo-64.png";

        var unsubscribeFooter = "";
        if (!string.IsNullOrEmpty(unsubscribeUrl))
        {
            unsubscribeFooter = $@"
                            <p style='margin: 8px 0 0 0; color: {TextTertiaryColor}; font-size: 12px; line-height: 1.5;'>
                                You are receiving this email because you have a Valour account.
                                <a href='{unsubscribeUrl}' style='color: {TextTertiaryColor}; text-decoration: underline;'>Unsubscribe from marketing emails</a>
                            </p>";
        }

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta name=""color-scheme"" content=""dark"">
    <meta name=""supported-color-schemes"" content=""dark"">
    <title>Valour</title>
</head>
<body style='margin: 0; padding: 0; background-color: {CanvasColor}; color: {TextSecondaryColor}; font-family: {FontStack};'>
    <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' bgcolor='{CanvasColor}' style='background-color: {CanvasColor};'>
        <tr>
            <td align='center' style='padding: 32px 16px;'>
                <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0' style='max-width: 600px;'>
                    <tr>
                        <td style='padding: 0 4px 20px 4px;'>
                            <img src='{logoUrl}' alt='Valour Logo' width='36' height='36' style='display: block; width: 36px; height: 36px; border: 0;'>
                        </td>
                    </tr>
                    <tr>
                        <td bgcolor='{SurfaceColor}' style='padding: 32px; background-color: {SurfaceColor}; border: 1px solid {LineColor}; border-radius: 10px; color: {TextSecondaryColor}; font-family: {FontStack}; font-size: 15px; line-height: 1.6;'>
                            {StyleLinks(bodyContent)}
                        </td>
                    </tr>
                    <tr>
                        <td style='padding: 20px 4px 0 4px; font-family: {FontStack};'>
                            <p style='margin: 0; color: {TextTertiaryColor}; font-size: 12px; line-height: 1.5;'>{address}</p>{unsubscribeFooter}
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }

    /// <summary>
    /// Gives links without inline styles, such as those in staff-written HTML,
    /// the link color so they stay readable on the dark email surface.
    /// </summary>
    private static string StyleLinks(string bodyContent)
    {
        if (string.IsNullOrEmpty(bodyContent))
            return bodyContent;

        return UnstyledLinkRegex().Replace(bodyContent, $"<a href='$1' style='{LinkStyle}'>");
    }
}
