using Valour.Shared.Models;

namespace Valour.Tests.Models;

public class BadgeVisibilityTests
{
    [Fact]
    public void Visibility_DefaultsToShownAndHonorsHiddenBit()
    {
        var user = new Valour.Database.User
        {
            HiddenBadgeFlags = (long)PlatformBadge.Stargazer,
        };

        Assert.False(PlatformBadgeCatalog.IsVisible(user, PlatformBadge.Stargazer));
        Assert.True(PlatformBadgeCatalog.IsVisible(user, PlatformBadge.Staff));
    }

    [Theory]
    [InlineData(22113735421460480, PlatformBadge.FirstOneThousand, true)]
    [InlineData(22113735421460481, PlatformBadge.FirstOneThousand, false)]
    [InlineData(22113735421460481, PlatformBadge.FirstTenThousand, true)]
    [InlineData(42076534464053249, PlatformBadge.FirstTenThousand, false)]
    public void MilestoneEligibility_UsesExclusiveBadgeRanges(
        long userId, PlatformBadge badge, bool expected)
    {
        var user = new Valour.Database.User { Id = userId };
        Assert.Equal(expected, PlatformBadgeCatalog.IsEarned(user, badge));
    }

    [Fact]
    public void PlatformLookup_AssignsOneUniqueBitPerBadge()
    {
        var flags = PlatformBadgeCatalog.Definitions.Keys.Select(x => (long)x).ToArray();

        Assert.Equal(flags.Length, flags.Distinct().Count());
        Assert.All(flags, flag => Assert.True(flag > 0 && (flag & (flag - 1)) == 0));
    }

    [Fact]
    public void PlanetMemberMask_IsIndependentFromPlatformMask()
    {
        var user = new Valour.Database.User { HiddenBadgeFlags = 0 };
        var member = new Valour.Database.PlanetMember
        {
            User = user,
            HiddenBadgeFlags = BadgeVisibility.FlagForBitIndex(0),
        };

        Assert.True(PlatformBadgeCatalog.IsVisible(user, PlatformBadge.Stargazer));
        Assert.False(BadgeVisibility.IsVisible(member.HiddenBadgeFlags, BadgeVisibility.FlagForBitIndex(0)));
    }
}
