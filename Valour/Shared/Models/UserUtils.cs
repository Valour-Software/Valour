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
            MailAddress address = new MailAddress(EmailDomainToDotCom(email));

            Console.WriteLine($"Email address: <{address.Address}>");

            return new TaskResult<string>(true, "Email was valid!", address.Address);
        }
        catch (FormatException e)
        {
            Console.WriteLine(e.Message);
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

    /// <summary>
    /// Returns the user's email domain as .com, filters for extended domains i.e. @example.test.me
    /// </summary>
    /// <param name="email"></param>
    /// <returns>
    /// Email as submitted or adjusted to .com
    /// </returns>
    private static string EmailDomainToDotCom(string email)
    {
        var dotCom = ".com";
        var atIndex = email.IndexOf('@');
        if (atIndex == -1)
        {
            return email;
        }
        var domain = email.Substring(atIndex + 1);


        if (!domain.EndsWith(dotCom))
        {
            var lastDotIndex = email.LastIndexOf('.');
            if (lastDotIndex <= atIndex)
            {
                return email;
            }

            email = email.Substring(0, lastDotIndex) + dotCom;
        }
        return email;
    }
}


