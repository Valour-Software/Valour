namespace Valour.Shared.Models;

/// <summary>
/// Platform badge bits stored on <see cref="ISharedUser.HiddenBadgeFlags"/>.
/// Values are permanent once assigned; renamed badges keep their original bit.
/// </summary>
[Flags]
public enum PlatformBadge : long
{
    None = 0,
    Stargazer = 1L << 0,
    FirstOneThousand = 1L << 1,
    FirstTenThousand = 1L << 2,
    Bot = 1L << 3,
    Staff = 1L << 4,
}

public readonly record struct PlatformBadgeDefinition(
    PlatformBadge Badge,
    string Key);

/// <summary>
/// Central lookup between durable platform badge bits and external identities.
/// Planet badge definitions can use the same bit-index approach per planet,
/// with each member's mask stored on ISharedPlanetMember.HiddenBadgeFlags.
/// </summary>
public static class PlatformBadgeCatalog
{
    public static readonly IReadOnlyDictionary<PlatformBadge, PlatformBadgeDefinition> Definitions =
        new Dictionary<PlatformBadge, PlatformBadgeDefinition>
        {
            [PlatformBadge.Stargazer] = new(PlatformBadge.Stargazer, "platform:stargazer"),
            [PlatformBadge.FirstOneThousand] = new(PlatformBadge.FirstOneThousand, "platform:first-1k"),
            [PlatformBadge.FirstTenThousand] = new(PlatformBadge.FirstTenThousand, "platform:first-10k"),
            [PlatformBadge.Bot] = new(PlatformBadge.Bot, "platform:bot"),
            [PlatformBadge.Staff] = new(PlatformBadge.Staff, "platform:staff"),
        };

    public static bool IsVisible(ISharedUser? user, PlatformBadge badge) =>
        user is not null && BadgeVisibility.IsVisible(user.HiddenBadgeFlags, (long)badge);

    public static bool IsEarned(ISharedUser user, PlatformBadge badge) => badge switch
    {
        PlatformBadge.Stargazer => user.SubscriptionType == UserSubscriptionTypes.Stargazer.Name ||
                                   user.SubscriptionType == UserSubscriptionTypes.StargazerPlus.Name ||
                                   user.SubscriptionType == UserSubscriptionTypes.StargazerPro.Name,
        PlatformBadge.FirstOneThousand => user.Id <= 22113735421460480,
        PlatformBadge.FirstTenThousand => user.Id > 22113735421460480 && user.Id <= 42076534464053248,
        PlatformBadge.Bot => user.Bot,
        PlatformBadge.Staff => user.ValourStaff,
        _ => false,
    };
}

public static class BadgeVisibility
{
    // Signed bigint gives each scope 63 non-negative, JSON-safe badge bits.
    public const int MaxBitIndex = 62;

    public static long FlagForBitIndex(int bitIndex)
    {
        if (bitIndex is < 0 or > MaxBitIndex)
            throw new ArgumentOutOfRangeException(nameof(bitIndex));
        return 1L << bitIndex;
    }

    public static bool IsVisible(long hiddenFlags, long badgeFlag) =>
        badgeFlag != 0 && (hiddenFlags & badgeFlag) == 0;

    public static long SetVisible(long hiddenFlags, long badgeFlag, bool visible) =>
        visible ? hiddenFlags & ~badgeFlag : hiddenFlags | badgeFlag;
}

public class SetUserBadgeVisibilityRequest
{
    public PlatformBadge Badge { get; set; }
    public bool Visible { get; set; }
}
