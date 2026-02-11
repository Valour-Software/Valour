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
    public static SendGridClient client;

    public static void SetupClient()
    {
        client = new SendGridClient(EmailConfig.Instance.ApiKey);
    }

    /// <summary>
    /// Sends an email using SendGrid API
    /// </summary>
    public static async Task<Response> SendEmailAsync(string address, string subject, string message, string html = null)
    {
        // Case if someone doesn't have an HTML version of email
        if (html == null)
        {
            html = message;
        }

        // Sender and recipient
        EmailAddress from = new EmailAddress("automated@valour.gg", "Valour");
        EmailAddress to = new EmailAddress(address);

        // Log to console
        Console.WriteLine($"Sending email to {address}.");

        SendGridMessage email = MailHelper.CreateSingleEmail(from, to, subject, message, html);

        // Privacy
        email.SetClickTracking(false, false);

        // Send the email
        return await client.SendEmailAsync(email);
    }
}
