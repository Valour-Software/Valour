using SendGrid;
using SendGrid.Helpers.Mail;
using Valour.Config.Configs;

namespace Valour.Server.Email;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class EmailManager
{
    private static SendGridClient? _client;

    public static bool IsConfigured => _client is not null;

    public static void SetupClient()
    {
        _client = CreateClient(EmailConfig.Instance?.ApiKey);

        if (_client is null)
            Console.WriteLine("Email delivery is disabled because no SendGrid API key is configured.");
    }

    internal static SendGridClient? CreateClient(string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) || apiKey == "fake-value"
            ? null
            : new SendGridClient(apiKey);

    private static SendGridClient GetClient() => _client ?? throw new InvalidOperationException(
        "Email delivery is disabled. Configure Email:ApiKey before sending email.");

    /// <summary>
    /// Sends an email using SendGrid API
    /// </summary>
    public static async Task<Response> SendEmailAsync(
        string address,
        string subject,
        string message,
        string html = null,
        CancellationToken cancellationToken = default)
    {
        // Case if someone doesn't have an HTML version of email
        if (html == null)
        {
            html = message;
        }

        // Sender and recipient
        EmailAddress from = new EmailAddress(EmailConfig.Instance.FromAddress, EmailConfig.Instance.FromName);
        EmailAddress to = new EmailAddress(address);

        // Log to console
        Console.WriteLine($"Sending email to {address}.");

        SendGridMessage email = MailHelper.CreateSingleEmail(from, to, subject, message, html);

        // Privacy
        email.SetClickTracking(false, false);

        // Send the email
        return await GetClient().SendEmailAsync(email, cancellationToken);
    }

    /// <summary>
    /// Sends a marketing email with List-Unsubscribe headers (RFC 8058).
    /// Marketing emails include unsubscribe mechanisms required by CAN-SPAM and Gmail/Yahoo 2024 rules.
    /// </summary>
    public static async Task<Response> SendMarketingEmailAsync(
        string address,
        string subject,
        string message,
        string html,
        string unsubscribeUrl,
        CancellationToken cancellationToken = default)
    {
        EmailAddress from = new EmailAddress(EmailConfig.Instance.FromAddress, EmailConfig.Instance.FromName);
        EmailAddress to = new EmailAddress(address);

        Console.WriteLine($"Sending marketing email to {address}.");

        SendGridMessage email = MailHelper.CreateSingleEmail(from, to, subject, message, html);

        // Privacy
        email.SetClickTracking(false, false);

        // RFC 8058 List-Unsubscribe headers
        email.AddHeader("List-Unsubscribe", $"<{unsubscribeUrl}>, <mailto:{EmailConfig.Instance.UnsubscribeAddress}>");
        email.AddHeader("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");

        return await GetClient().SendEmailAsync(email, cancellationToken);
    }
}
