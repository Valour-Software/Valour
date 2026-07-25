using Microsoft.EntityFrameworkCore;
using Valour.Database.Context;
using Valour.Server.Models.Economy;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Services.Villages;

/// <summary>
/// Buying and selling village land and buildings with the planet's own currency.
///
/// Money movement and ownership are two separate commits, because
/// <see cref="EcoService.CreateTransactionAsync"/> opens and commits its own
/// database transaction and cannot enlist in an ambient one. The order is
/// therefore chosen so that the recoverable failure is the one that can happen:
/// the buyer is charged first, then ownership is transferred. If the process
/// dies in between, the sale is completed by simply retrying - the transaction
/// fingerprint is derived from the sale rather than random, so the second
/// attempt cannot double-charge and instead proceeds straight to the handover.
///
/// Doing it the other way round would hand over the deed and then fail to take
/// payment, which is not recoverable without clawing property back.
/// </summary>
public class VillageMarketService
{
    private readonly ValourDb _db;
    private readonly EcoService _ecoService;
    private readonly CoreHubService _hubService;
    private readonly ILogger<VillageMarketService> _logger;

    public VillageMarketService(
        ValourDb db,
        EcoService ecoService,
        CoreHubService hubService,
        ILogger<VillageMarketService> logger)
    {
        _db = db;
        _ecoService = ecoService;
        _hubService = hubService;
        _logger = logger;
    }

    /// <summary>
    /// Derived from the sale rather than random so a retry after a partial
    /// failure is recognised as the same purchase. The unique index on
    /// Fingerprint is what actually enforces it.
    /// </summary>
    internal static string BuildFingerprint(string kind, long assetId, long buyerMemberId, long? sellerMemberId) =>
        $"village:{kind}:{assetId}:{buyerMemberId}:{sellerMemberId?.ToString() ?? "planet"}";

    public async Task<TaskResult> SetPlotForSaleAsync(long plotId, long planetId, bool forSale, decimal price)
    {
        var plot = await _db.VillagePlots.FirstOrDefaultAsync(x => x.Id == plotId && x.PlanetId == planetId);
        if (plot is null)
            return new TaskResult(false, "Plot not found.");

        if (price < 0)
            return new TaskResult(false, "Price cannot be negative.");

        plot.ForSale = forSale;
        plot.Price = price;
        await _db.SaveChangesAsync();

        _hubService.NotifyPlanetItemChange(planetId, plot.ToModel());
        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> SetBuildingForSaleAsync(long buildingId, long planetId, bool forSale, decimal price)
    {
        var building = await _db.VillageBuildings.FirstOrDefaultAsync(x => x.Id == buildingId && x.PlanetId == planetId);
        if (building is null)
            return new TaskResult(false, "Building not found.");

        if (price < 0)
            return new TaskResult(false, "Price cannot be negative.");

        building.ForSale = forSale;
        building.Price = price;
        await _db.SaveChangesAsync();

        _hubService.NotifyPlanetItemChange(planetId, building.ToModel());
        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> PurchasePlotAsync(long plotId, long planetId, long buyerMemberId, long buyerUserId)
    {
        var plot = await _db.VillagePlots.FirstOrDefaultAsync(x => x.Id == plotId && x.PlanetId == planetId);
        if (plot is null)
            return new TaskResult(false, "Plot not found.");

        var check = ValidateSale(plot.ForSale, plot.OwnerMemberId, buyerMemberId);
        if (!check.Success)
            return check;

        var payment = await SettlePaymentAsync(
            "plot", plot.Id, planetId, plot.Price, plot.OwnerMemberId, buyerMemberId, buyerUserId, plot.Name);

        if (!payment.Success)
            return payment;

        plot.OwnerMemberId = buyerMemberId;
        plot.ForSale = false;
        await _db.SaveChangesAsync();

        _hubService.NotifyPlanetItemChange(planetId, plot.ToModel());
        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> PurchaseBuildingAsync(long buildingId, long planetId, long buyerMemberId, long buyerUserId)
    {
        var building = await _db.VillageBuildings.FirstOrDefaultAsync(x => x.Id == buildingId && x.PlanetId == planetId);
        if (building is null)
            return new TaskResult(false, "Building not found.");

        var check = ValidateSale(building.ForSale, building.OwnerMemberId, buyerMemberId);
        if (!check.Success)
            return check;

        var payment = await SettlePaymentAsync(
            "building", building.Id, planetId, building.Price, building.OwnerMemberId, buyerMemberId, buyerUserId, building.Name);

        if (!payment.Success)
            return payment;

        building.OwnerMemberId = buyerMemberId;
        building.ForSale = false;
        await _db.SaveChangesAsync();

        _hubService.NotifyPlanetItemChange(planetId, building.ToModel());
        return TaskResult.SuccessResult;
    }

    internal static TaskResult ValidateSale(bool forSale, long? ownerMemberId, long buyerMemberId)
    {
        if (!forSale)
            return new TaskResult(false, "This is not for sale.");

        if (ownerMemberId == buyerMemberId)
            return new TaskResult(false, "You already own this.");

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Moves the money, or confirms it has already moved. Returns success for a
    /// free listing without touching the ledger at all - a zero-value transfer
    /// would be rejected by the economy and is not worth recording.
    /// </summary>
    private async Task<TaskResult> SettlePaymentAsync(
        string kind,
        long assetId,
        long planetId,
        decimal price,
        long? sellerMemberId,
        long buyerMemberId,
        long buyerUserId,
        string assetName)
    {
        if (price <= 0)
            return TaskResult.SuccessResult;

        var fingerprint = BuildFingerprint(kind, assetId, buyerMemberId, sellerMemberId);

        // A matching fingerprint means the buyer was already charged for this
        // exact sale and the previous attempt died before the handover.
        var alreadyPaid = await _db.Transactions.AnyAsync(x => x.Fingerprint == fingerprint);
        if (alreadyPaid)
        {
            _logger.LogInformation(
                "Village {Kind} {AssetId}: payment already settled under fingerprint {Fingerprint}, completing handover.",
                kind, assetId, fingerprint);

            return TaskResult.SuccessResult;
        }

        var currency = await _ecoService.GetPlanetCurrencyAsync(planetId);
        if (currency is null)
            return new TaskResult(false, "This planet has no currency, so nothing can be sold.");

        var buyerAccount = await _ecoService.GetUserAccountAsync(buyerUserId, planetId);
        if (buyerAccount is null)
            return new TaskResult(false, "You do not have an account for this planet's currency.");

        var seller = await ResolveSellerAccountAsync(planetId, sellerMemberId);
        if (seller is null)
            return new TaskResult(false, "The seller has no account to receive payment.");

        var transaction = new Transaction
        {
            Id = Guid.NewGuid().ToString(),
            PlanetId = planetId,
            UserFromId = buyerUserId,
            AccountFromId = buyerAccount.Id,
            UserToId = seller.UserId,
            AccountToId = seller.Id,
            TimeStamp = DateTime.UtcNow,
            Description = $"Purchase of {assetName}",
            Amount = price,
            Data = $"village:{kind}:{assetId}",
            Fingerprint = fingerprint,
        };

        var result = await _ecoService.CreateTransactionAsync(transaction);
        if (!result.Success)
            return new TaskResult(false, result.Message);

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Unowned property is sold by the planet itself, so the proceeds go to a
    /// shared account rather than vanishing.
    /// </summary>
    private async Task<EcoAccount> ResolveSellerAccountAsync(long planetId, long? sellerMemberId)
    {
        if (sellerMemberId is not null)
        {
            var member = await _db.PlanetMembers
                .FirstOrDefaultAsync(x => x.Id == sellerMemberId.Value && x.PlanetId == planetId);

            if (member is not null)
            {
                var account = await _ecoService.GetUserAccountAsync(member.UserId, planetId);
                if (account is not null)
                    return account;
            }
        }

        var shared = await _db.EcoAccounts
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.AccountType == AccountType.Shared);

        return shared.ToModel();
    }
}
