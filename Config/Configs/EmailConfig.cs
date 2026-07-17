namespace Valour.Config.Configs;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public class EmailConfig
{
    /// <summary>
    /// The current Email Configuration
    /// </summary>
    public static EmailConfig Instance { get; private set; }

    /// <summary>
    /// The API key for the email service
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// True when a real email provider is configured. When false (no key, or
    /// the test placeholder "fake-value"), accounts are auto-verified and no
    /// email is sent.
    /// </summary>
    public static bool IsEnabled =>
        !string.IsNullOrWhiteSpace(Instance?.ApiKey) && Instance.ApiKey != "fake-value";

    /// <summary>
    /// HMAC-SHA256 secret for signing unsubscribe tokens
    /// </summary>
    public string UnsubscribeSecret { get; set; }

    /// <summary>
    /// CAN-SPAM physical mailing address
    /// </summary>
    public string PhysicalAddress { get; set; } = "99 Wall Street Suite 1299, New York, NY";

    /// <summary>
    /// Sender address for outgoing email
    /// </summary>
    public string FromAddress { get; set; } = "automated@valour.gg";

    /// <summary>
    /// Sender display name for outgoing email
    /// </summary>
    public string FromName { get; set; } = "Valour";

    /// <summary>
    /// Mailto address used in List-Unsubscribe headers
    /// </summary>
    public string UnsubscribeAddress { get; set; } = "unsubscribe@valour.gg";

    /// <summary>
    /// Absolute URL of the logo shown in email templates
    /// </summary>
    public string LogoUrl { get; set; } = "https://valour.gg/media/logo/logo-64.png";

    /// <summary>
    /// Set instance to newest config
    /// </summary>
    public EmailConfig()
    {
        Instance = this;
    }
}
