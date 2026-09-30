using Microsoft.AspNetCore.Mvc;
using Valour.Config.Configs;
using Valour.Server.Email;
using DbUserPreferences = Valour.Database.UserPreferences;

namespace Valour.Server.Api.Dynamic;

public class UnsubscribeApi
{
    /// <summary>
    /// Link-click unsubscribe from email body; returns an HTML confirmation page.
    /// No authentication required (token-based).
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/email/unsubscribe")]
    public static async Task<IResult> UnsubscribeViaLink(
        [FromQuery] string token,
        ValourDb db)
    {
        var userId = UnsubscribeTokenService.ValidateToken(token);
        if (userId is null)
            return Results.Content(BuildHtmlPage("Invalid Link", "This unsubscribe link is invalid or has expired."), "text/html");

        await OptOutUserAsync(userId.Value, db);

        return Results.Content(BuildHtmlPage("Unsubscribed",
            "You have been unsubscribed from Valour marketing emails. You will still receive transactional emails (password resets, account verification)."),
            "text/html");
    }

    /// <summary>
    /// RFC 8058 one-click unsubscribe: email clients call this directly via POST.
    /// No authentication required (token-based).
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/email/unsubscribe/oneclick")]
    public static async Task<IResult> UnsubscribeOneClick(
        [FromQuery] string token,
        ValourDb db)
    {
        var userId = UnsubscribeTokenService.ValidateToken(token);
        if (userId is null)
            return ValourResult.BadRequest("Invalid unsubscribe token.");

        await OptOutUserAsync(userId.Value, db);

        return ValourResult.Ok("Unsubscribed successfully.");
    }

    /// <summary>
    /// Authenticated toggle for marketing email preferences from app settings.
    /// </summary>
    [UserRequired]
    [ValourRoute(HttpVerbs.Post, "api/users/me/preferences/marketingEmails/{enabled}")]
    public static async Task<IResult> SetMarketingEmails(
        bool enabled,
        UserService userService,
        ValourDb db)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        if (userId == long.MinValue)
            return ValourResult.NoToken();

        var prefs = await db.UserPreferences.FindAsync(userId);
        if (prefs is null)
        {
            prefs = new DbUserPreferences
            {
                Id = userId,
                MarketingEmailOptOut = !enabled
            };
            db.UserPreferences.Add(prefs);
        }
        else
        {
            prefs.MarketingEmailOptOut = !enabled;
        }

        await db.SaveChangesAsync();

        return ValourResult.Ok(enabled ? "Marketing emails enabled." : "Marketing emails disabled.");
    }

    /// <summary>
    /// Creates or updates the UserPreferences row to opt out the user from marketing emails.
    /// </summary>
    private static async Task OptOutUserAsync(long userId, ValourDb db)
    {
        var prefs = await db.UserPreferences.FindAsync(userId);
        if (prefs is null)
        {
            prefs = new DbUserPreferences
            {
                Id = userId,
                MarketingEmailOptOut = true
            };
            db.UserPreferences.Add(prefs);
        }
        else
        {
            prefs.MarketingEmailOptOut = true;
        }

        await db.SaveChangesAsync();
    }

    private static string BuildHtmlPage(string title, string message)
    {
        var logoUrl = EmailConfig.Instance?.LogoUrl ?? "https://valour.gg/media/logo/logo-64.png";

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta name=""color-scheme"" content=""dark"">
    <meta name=""theme-color"" content=""{EmailTemplateHelper.CanvasColor}"">
    <title>{title} - Valour</title>
    <style>
        @font-face {{
            font-family: ""Instrument Sans"";
            font-style: normal;
            font-weight: 400 700;
            font-display: swap;
            src: url(""/_content/Valour.Client/css/fonts/instrument-sans-latin.woff2"") format(""woff2"");
        }}
    </style>
</head>
<body style='margin: 0; padding: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center; background-color: {EmailTemplateHelper.CanvasColor}; color: {EmailTemplateHelper.TextPrimaryColor}; font-family: {EmailTemplateHelper.FontStack}; -webkit-font-smoothing: antialiased;'>
    <main style='box-sizing: border-box; width: calc(100% - 32px); max-width: 440px; margin: 40px 16px; padding: 32px; background-color: {EmailTemplateHelper.SurfaceColor}; border: 1px solid {EmailTemplateHelper.LineColor}; border-radius: 14px; text-align: center;'>
        <img src='{logoUrl}' alt='Valour Logo' width='40' height='40' style='display: block; width: 40px; height: 40px; margin: 0 auto 20px;'>
        <h1 style='margin: 0 0 10px 0; color: {EmailTemplateHelper.TextPrimaryColor}; font-size: 22px; font-weight: 600; line-height: 1.3; letter-spacing: -0.015em;'>{title}</h1>
        <p style='margin: 0; color: {EmailTemplateHelper.TextSecondaryColor}; font-size: 15px; line-height: 1.6;'>{message}</p>
    </main>
</body>
</html>";
    }
}
