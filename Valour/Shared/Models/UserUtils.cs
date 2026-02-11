using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Valour.Shared.Models;

public class UserUtils
{
    // Matches control characters, zero-width characters, and other invisible Unicode
    private static readonly Regex InvisibleCharsRegex = new Regex(
        @"[\u0000-\u001F\u007F-\u009F\u00AD\u200B-\u200F\u2028-\u202F\u2060\uFEFF\uFFF9-\uFFFB]");

    /// <summary>
    /// Sanitizes an email address by trimming whitespace, removing invisible
    /// characters, and normalizing to lowercase.
    /// Returns the sanitized email, or null if the input is null/empty.
    /// </summary>
    public static string SanitizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        email = email.Trim().ToLower();
        email = InvisibleCharsRegex.Replace(email, string.Empty);

        return email;
    }

    /// <summary>
    /// Validates an email address. Returns the normalized address from MailAddress
    /// on success. The caller should use the returned address (Data) for storage
    /// since it is the canonical form.
    /// </summary>
    public static TaskResult<string> TestEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new TaskResult<string>(false, "Email was invalid.");

        try
        {
            // Validate the actual email, not a domain-munged version
            var address = new MailAddress(email);

            // Reject display-name tricks like '"Name" <real@email.com>'
            if (address.Address != email)
                return new TaskResult<string>(false, "Email was invalid.");

            // Require a TLD (at least one dot after @)
            var domain = email[(email.IndexOf('@') + 1)..];
            if (!domain.Contains('.'))
                return new TaskResult<string>(false, "Email was invalid.");

            return new TaskResult<string>(true, "Email was valid!", address.Address);
        }
        catch (FormatException)
        {
            return new TaskResult<string>(false, "Email was invalid.");
        }
    }

    private static readonly Regex UsernameRegex = new Regex(@"^[a-zA-Z0-9_-]+$");

    /// <summary>
    /// Checks if a username meets standards
    /// </summary>
    public static TaskResult TestUsername(string username)
    {
        if (username.Length > 32)
        {
            return new TaskResult(false, "That username is too long!");
        }

        return new TaskResult(true, "The given username is valid.");
    }

    private static readonly Regex HasUpperRegex = new Regex(@"[A-Z]");
    private static readonly Regex HasLowerRegex = new Regex(@"[a-z]");
    private static readonly Regex HasNumbersRegex = new Regex(@"\d");
    private static readonly Regex HasSymbolsRegex = new Regex(@"\W");

    private static readonly TaskResult PasswordFailedResult =
        new TaskResult(false, $"Password must be 10+ characters, with an uppercase letter, lowercase letter, and number or symbol.");

    /// <summary>
    /// Returns success if a password meets complexity rules
    /// </summary>
    public static TaskResult TestPasswordComplexity(string password)
    {
        if (password.Length < 10 || // Length
            !HasUpperRegex.IsMatch(password) || // Uppercase
            !HasLowerRegex.IsMatch(password) || // Lowercase
            (!HasNumbersRegex.IsMatch(password) && !HasSymbolsRegex.IsMatch(password))) // Numbers or symbols
        {
            return PasswordFailedResult;
        }

        return new TaskResult(true, $"Success: The given password passed all tests.");
    }
}
