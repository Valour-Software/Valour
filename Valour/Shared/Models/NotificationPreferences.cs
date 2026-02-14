using System;
using System.Linq;

namespace Valour.Shared.Models;

public static class NotificationPreferences
{
    // Internal metadata bit used to distinguish a legacy unset mask from an explicit all-disabled mask.
    private const long InitializationFlag = 1L << 62;

    public const int MinNotificationVolume = 0;
    public const int MaxNotificationVolume = 100;
    public const int DefaultNotificationVolume = 20;

    public static readonly NotificationSource[] ConfigurableSources =
    [
        NotificationSource.Platform,
        NotificationSource.DirectMention,
        NotificationSource.DirectReply,
        NotificationSource.PlanetMemberMention,
        NotificationSource.PlanetMemberReply,
        NotificationSource.PlanetRoleMention,
        NotificationSource.PlanetHereMention,
        NotificationSource.PlanetEveryoneMention,
        NotificationSource.FriendRequest,
        NotificationSource.FriendRequestAccepted,
        NotificationSource.TransactionReceived,
        NotificationSource.TradeProposed,
        NotificationSource.TradeAccepted,
        NotificationSource.TradeDeclined,
        NotificationSource.DirectMessage
    ];

    public static readonly long AllNotificationSourcesMask = ConfigurableSources
        .Aggregate(0L, (current, source) => current | (long)source);

    public static int ClampVolume(int volume)
    {
        return Math.Clamp(volume, MinNotificationVolume, MaxNotificationVolume);
    }

    private static bool HasInitializationFlag(long enabledSourcesMask)
    {
        return (enabledSourcesMask & InitializationFlag) == InitializationFlag;
    }

    private static long GetSourceBits(long enabledSourcesMask)
    {
        return enabledSourcesMask & ~InitializationFlag;
    }

    public static bool IsSingleSource(NotificationSource source)
    {
        var value = (long)source;
        return value > 0 && (value & (value - 1)) == 0;
    }

    public static bool IsConfigurableSource(NotificationSource source)
    {
        return ConfigurableSources.Contains(source);
    }

    public static bool IsSourceEnabled(long enabledSourcesMask, NotificationSource source)
    {
        if (!IsSingleSource(source))
            return true;

        var sourceBits = GetSourceBits(enabledSourcesMask);

        // Legacy rows may store 0 when notification source settings were unset.
        // Default behavior should be all-on until the user explicitly changes a source.
        if (!HasInitializationFlag(enabledSourcesMask) && sourceBits == 0)
            return true;

        var sourceValue = (long)source;
        return (sourceBits & sourceValue) == sourceValue;
    }

    public static long SetSourceEnabled(long enabledSourcesMask, NotificationSource source, bool enabled)
    {
        if (!IsSingleSource(source))
            return enabledSourcesMask;

        var sourceBits = GetSourceBits(enabledSourcesMask);

        // Initialize legacy unset values to default all-on before applying the first explicit toggle.
        if (!HasInitializationFlag(enabledSourcesMask))
            sourceBits = sourceBits == 0 ? AllNotificationSourcesMask : sourceBits;

        var sourceValue = (long)source;

        if (enabled)
            sourceBits |= sourceValue;
        else
            sourceBits &= ~sourceValue;

        return sourceBits | InitializationFlag;
    }
}
