using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Valour.Shared.Models;

public class UserUtils
{
    /// <summary>
    /// Allows checking if a email meets standards
    /// </summary>
    public static TaskResult<string> TestEmail(string email)
    {
        try
        {
            MailAddress address = new MailAddress(email);

            Console.WriteLine($"Email address: <{address.Address}>");

            return new TaskResult<string>(true, "Email was valid!", address.Address);
        }
        catch (FormatException e)
        {
            Console.WriteLine(e.Message);
            return new TaskResult<string>(false, "Email was invalid.");
        }
    }

    public static Regex usernameRegex = new Regex(@"^[a-zA-Z0-9_-]+$");

    /// <summary>
    /// Checks if a username meets standards
    /// </summary>
    public static TaskResult TestUsername(string username)
    {
        if (username.Length > 32)
        {
            return new TaskResult(false, "That username is too long!");
        }

        if (!usernameRegex.IsMatch(username))
        {
            return new TaskResult(false, "Usernames must be alphanumeric plus underscores and dashes.");
        }

        return new TaskResult(true, "The given username is valid.");
    }

    public static Regex hasUpper = new Regex(@"[A-Z]");
    public static Regex hasLower = new Regex(@"[a-z]");
    public static Regex hasNumbers = new Regex(@"\d");
    public static Regex hasSymbols = new Regex(@"\W");

    /// <summary>
    /// Returns success if a password meets complexity rules
    /// </summary>
    public static TaskResult TestPasswordComplexity(string password)
    {
        if (password.Length < 12)
        {
            return new TaskResult(false, $"Failed: Please use a password at least 12 characters in length.");
        }

        if (!hasUpper.IsMatch(password))
        {
            return new TaskResult(false, $"Failed: Please use a password that contains an uppercase character.");
        }

        if (!hasLower.IsMatch(password))
        {
            return new TaskResult(false, $"Failed: Please use a password that contains an lowercase character.");
        }

        if (!hasNumbers.IsMatch(password))
        {
            return new TaskResult(false, $"Failed: Please use a password that contains a number.");
        }

        if (!hasSymbols.IsMatch(password))
        {
            return new TaskResult(false, $"Failed: Please use a password that contains a symbol.");
        }

        return new TaskResult(true, $"Success: The given password passed all tests.");
    }

}


