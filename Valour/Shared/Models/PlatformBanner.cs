using System.Security.Cryptography;
using System.Text;

namespace Valour.Shared.Models;

public enum PlatformBannerKind
{
    Information = 0,
    Warning = 1,
    Critical = 2
}

public sealed class PlatformBanner
{
    public string Title { get; set; }
    public string Message { get; set; }
    public PlatformBannerKind Kind { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string Hash { get; set; }

    public static string ComputeHash(string title, string message, PlatformBannerKind kind)
    {
        var value = $"{(int)kind}\n{title?.Trim()}\n{message?.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
    }
}

public sealed class SetPlatformBannerRequest
{
    public string Title { get; set; }
    public string Message { get; set; }
    public PlatformBannerKind Kind { get; set; }
}

public sealed class PlatformBannerUpdate
{
    public PlatformBanner Banner { get; set; }
}
