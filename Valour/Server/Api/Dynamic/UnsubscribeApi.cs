using Microsoft.AspNetCore.Mvc;
using Valour.Server.Email;
using DbUserPreferences = Valour.Database.UserPreferences;

namespace Valour.Server.Api.Dynamic;

public class UnsubscribeApi
{
    /// <summary>
    /// Link-click unsubscribe from email body — returns an HTML confirmation page.
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
    /// RFC 8058 one-click unsubscribe — email clients call this directly via POST.
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
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{title} - Valour</title>
</head>
<body style='font-family: Outfit, Arial, sans-serif; margin: 0; padding: 0; background-color: #f4f4f4;'>
    <div style='max-width: 600px; margin: 40px auto; background-color: #fff; padding: 30px; border-radius: 5px; box-shadow: 0 0 10px rgba(0, 0, 0, 0.1); text-align: center;'>
        <img src='https://valour.gg/media/logo/logo-64.png' alt='Valour Logo' style='max-width: 64px; height: auto; display: block; margin: 0 auto 20px;'>
        <h1 style='color: #333;'>{title}</h1>
        <p style='color: #666;'>{message}</p>
    </div>
</body>
</html>";
    }
}
