using System.Security.Cryptography;
using System.Text;
using Valour.Config.Configs;

namespace Valour.Server.Email;

public static class UnsubscribeTokenService
{
    /// <summary>
    /// Generates a stateless unsubscribe token for the given user.
    /// Format: "{userId}.{HMAC-SHA256 hex}"
    /// </summary>
    public static string GenerateToken(long userId)
    {
        var secret = EmailConfig.Instance.UnsubscribeSecret;
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("UnsubscribeSecret is not configured.");

        var message = userId.ToString();
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var messageBytes = Encoding.UTF8.GetBytes(message);

        var hash = HMACSHA256.HashData(keyBytes, messageBytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();

        return $"{userId}.{hex}";
    }

    /// <summary>
    /// Validates an unsubscribe token and returns the userId if valid, or null if invalid.
    /// Uses timing-safe comparison to prevent timing attacks.
    /// </summary>
    public static long? ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        var dotIndex = token.IndexOf('.');
        if (dotIndex < 1 || dotIndex >= token.Length - 1)
            return null;

        var userIdPart = token[..dotIndex];
        var hmacPart = token[(dotIndex + 1)..];

        if (!long.TryParse(userIdPart, out var userId))
            return null;

        var secret = EmailConfig.Instance.UnsubscribeSecret;
        if (string.IsNullOrEmpty(secret))
            return null;

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var messageBytes = Encoding.UTF8.GetBytes(userIdPart);

        var expectedHash = HMACSHA256.HashData(keyBytes, messageBytes);
        var expectedHex = Convert.ToHexString(expectedHash).ToLowerInvariant();

        var expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
        var actualBytes = Encoding.UTF8.GetBytes(hmacPart);

        if (expectedBytes.Length != actualBytes.Length)
            return null;

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
            return null;

        return userId;
    }
}
