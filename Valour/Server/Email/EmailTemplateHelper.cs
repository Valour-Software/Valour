using Valour.Config.Configs;

namespace Valour.Server.Email;

public static class EmailTemplateHelper
{
    /// <summary>
    /// Wraps email body content in a proper HTML document structure with consistent styling.
    /// If unsubscribeUrl is provided (marketing emails), adds an unsubscribe link in the footer.
    /// Transactional emails (registration, password reset) should pass null for unsubscribeUrl.
    /// </summary>
    public static string WrapInTemplate(string bodyContent, string unsubscribeUrl = null)
    {
        var address = EmailConfig.Instance?.PhysicalAddress ?? "99 Wall Street Suite 1299, New York, NY";

        var unsubscribeFooter = "";
        if (!string.IsNullOrEmpty(unsubscribeUrl))
        {
            unsubscribeFooter = $@"
                <p style='color: #999; font-size: 12px; margin-top: 20px;'>
                    You are receiving this email because you have a Valour account.
                    <a href='{unsubscribeUrl}' style='color: #999; text-decoration: underline;'>Unsubscribe from marketing emails</a>
                </p>";
        }

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Valour</title>
</head>
<body style='font-family: Outfit, Arial, sans-serif; margin: 0; padding: 0; background-color: #f4f4f4;'>
    <div style='max-width: 600px; margin: 20px auto; background-color: #fff; padding: 20px; border-radius: 5px; box-shadow: 0 0 10px rgba(0, 0, 0, 0.1);'>
        <img src='{EmailConfig.Instance?.LogoUrl ?? "https://valour.gg/media/logo/logo-64.png"}' alt='Valour Logo' style='max-width: 100%; height: auto; display: block; margin: 0 auto;'>
        {bodyContent}
        <hr style='border: none; border-top: 1px solid #eee; margin: 30px 0 15px 0;'>
        <p style='color: #999; font-size: 12px;'>{address}</p>{unsubscribeFooter}
    </div>
</body>
</html>";
    }
}
