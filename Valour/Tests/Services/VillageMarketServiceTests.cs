using Valour.Server.Services.Villages;

namespace Valour.Tests.Services;

/// <summary>
/// Covers the sale rules that protect a buyer's money.
///
/// The fingerprint is the load-bearing piece: payment and the ownership handover
/// are two separate commits, so a retry after a partial failure must be
/// recognised as the same purchase rather than charging again.
/// </summary>
public class VillageMarketServiceTests
{
    [Fact]
    public void Fingerprint_IsStableForTheSameSale()
    {
        // A retry must produce the same fingerprint, or the unique index will
        // not stop the buyer being charged twice.
        var first = VillageMarketService.BuildFingerprint("plot", 10, buyerMemberId: 5, sellerMemberId: 7);
        var second = VillageMarketService.BuildFingerprint("plot", 10, buyerMemberId: 5, sellerMemberId: 7);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Fingerprint_DiffersPerBuyer()
    {
        var buyerA = VillageMarketService.BuildFingerprint("plot", 10, buyerMemberId: 5, sellerMemberId: 7);
        var buyerB = VillageMarketService.BuildFingerprint("plot", 10, buyerMemberId: 6, sellerMemberId: 7);

        Assert.NotEqual(buyerA, buyerB);
    }

    [Fact]
    public void Fingerprint_DiffersPerAsset()
    {
        var plot = VillageMarketService.BuildFingerprint("plot", 10, 5, 7);
        var building = VillageMarketService.BuildFingerprint("building", 10, 5, 7);

        // Same id in different tables must not collide.
        Assert.NotEqual(plot, building);

        Assert.NotEqual(
            VillageMarketService.BuildFingerprint("plot", 10, 5, 7),
            VillageMarketService.BuildFingerprint("plot", 11, 5, 7));
    }

    [Fact]
    public void Fingerprint_DistinguishesResaleByTheSameBuyer()
    {
        // Buying a plot, selling it on, then buying it back is a genuinely new
        // sale and must not be mistaken for a retry of the first one.
        var fromPlanet = VillageMarketService.BuildFingerprint("plot", 10, buyerMemberId: 5, sellerMemberId: null);
        var fromMember = VillageMarketService.BuildFingerprint("plot", 10, buyerMemberId: 5, sellerMemberId: 9);

        Assert.NotEqual(fromPlanet, fromMember);
    }

    [Fact]
    public void ValidateSale_RejectsWhatIsNotListed()
    {
        var result = VillageMarketService.ValidateSale(forSale: false, ownerMemberId: 7, buyerMemberId: 5);

        Assert.False(result.Success);
        Assert.Contains("not for sale", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSale_RejectsBuyingYourOwnProperty()
    {
        var result = VillageMarketService.ValidateSale(forSale: true, ownerMemberId: 5, buyerMemberId: 5);

        Assert.False(result.Success);
        Assert.Contains("already own", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateSale_AllowsBuyingUnclaimedLand()
    {
        var result = VillageMarketService.ValidateSale(forSale: true, ownerMemberId: null, buyerMemberId: 5);

        Assert.True(result.Success);
    }

    [Fact]
    public void ValidateSale_AllowsBuyingFromAnotherMember()
    {
        var result = VillageMarketService.ValidateSale(forSale: true, ownerMemberId: 7, buyerMemberId: 5);

        Assert.True(result.Success);
    }

    [Fact]
    public void Listing_AllowsThePropertyOwner()
    {
        Assert.True(VillageMarketService.CanManageListing(
            ownerMemberId: 7,
            actorMemberId: 7,
            canManageVillage: false));
    }

    [Fact]
    public void Listing_RejectsUnrelatedMemberButAllowsManager()
    {
        Assert.False(VillageMarketService.CanManageListing(
            ownerMemberId: 7,
            actorMemberId: 9,
            canManageVillage: false));

        Assert.True(VillageMarketService.CanManageListing(
            ownerMemberId: 7,
            actorMemberId: 9,
            canManageVillage: true));
    }
}
