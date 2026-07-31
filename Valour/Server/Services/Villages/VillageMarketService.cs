using System.Collections.Concurrent;
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
    // Planets are node-pinned, so serializing an asset's purchase on this node
    // prevents two buyers from both settling before either deed handover lands.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AssetLocks = new();

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
    internal static string BuildFingerprint(
        string kind,
        long assetId,
        string saleId,
        long buyerMemberId,
        long? sellerMemberId) =>
        $"village:{kind}:{assetId}:{saleId}:{buyerMemberId}:{sellerMemberId?.ToString() ?? "planet"}";

    public async Task<TaskResult> SetPlotForSaleAsync(
        long plotId,
        long planetId,
        long actorMemberId,
        bool canManageVillage,
        bool forSale,
        decimal price)
    {
        var gate = GetAssetLock("plot", planetId, plotId);
        await gate.WaitAsync();
        try
        {
            var plot = await _db.VillagePlots.FirstOrDefaultAsync(x => x.Id == plotId && x.PlanetId == planetId);
            if (plot is null)
                return new TaskResult(false, "Plot not found.");

            if (!CanManageListing(plot.OwnerMemberId, actorMemberId, canManageVillage))
                return new TaskResult(false, "Only the owner or a village manager can list this parcel.");

            if (price < 0)
                return new TaskResult(false, "Price cannot be negative.");

            if (forSale && (!plot.ForSale || string.IsNullOrWhiteSpace(plot.SaleId)))
                plot.SaleId = CreateSaleId();

            plot.ForSale = forSale;
            plot.Price = price;
            await _db.SaveChangesAsync();

            _hubService.NotifyPlanetItemChange(planetId, plot.ToModel());
            return TaskResult.SuccessResult;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TaskResult> SetBuildingForSaleAsync(
        long buildingId,
        long planetId,
        long actorMemberId,
        bool canManageVillage,
        bool forSale,
        decimal price)
    {
        var gate = GetAssetLock("building", planetId, buildingId);
        await gate.WaitAsync();
        try
        {
            var building = await _db.VillageBuildings.FirstOrDefaultAsync(x => x.Id == buildingId && x.PlanetId == planetId);
            if (building is null)
                return new TaskResult(false, "Building not found.");

            if (!CanManageListing(building.OwnerMemberId, actorMemberId, canManageVillage))
                return new TaskResult(false, "Only the owner or a village manager can list this building.");

            if (price < 0)
                return new TaskResult(false, "Price cannot be negative.");

            if (forSale && (!building.ForSale || string.IsNullOrWhiteSpace(building.SaleId)))
                building.SaleId = CreateSaleId();

            building.ForSale = forSale;
            building.Price = price;
            await _db.SaveChangesAsync();

            _hubService.NotifyPlanetItemChange(planetId, building.ToModel());
            return TaskResult.SuccessResult;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TaskResult> PurchasePlotAsync(long plotId, long planetId, long buyerMemberId, long buyerUserId)
    {
        var gate = GetAssetLock("plot", planetId, plotId);
        await gate.WaitAsync();
        try
        {
            return await PurchasePlotCoreAsync(plotId, planetId, buyerMemberId, buyerUserId);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TaskResult> PurchasePlotCoreAsync(
        long plotId,
        long planetId,
        long buyerMemberId,
        long buyerUserId)
    {
        var plot = await _db.VillagePlots.FirstOrDefaultAsync(x => x.Id == plotId && x.PlanetId == planetId);
        if (plot is null)
            return new TaskResult(false, "Plot not found.");

        var check = ValidateSale(plot.ForSale, plot.OwnerMemberId, buyerMemberId);
        if (!check.Success)
            return check;

        if (string.IsNullOrWhiteSpace(plot.SaleId))
        {
            plot.SaleId = CreateSaleId();
            await _db.SaveChangesAsync();
        }

        var payment = await SettlePaymentAsync(
            "plot", plot.Id, plot.SaleId, planetId, plot.Price, plot.OwnerMemberId, buyerMemberId, buyerUserId, plot.Name);

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
        var gate = GetAssetLock("building", planetId, buildingId);
        await gate.WaitAsync();
        try
        {
            return await PurchaseBuildingCoreAsync(buildingId, planetId, buyerMemberId, buyerUserId);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TaskResult> PurchaseBuildingCoreAsync(
        long buildingId,
        long planetId,
        long buyerMemberId,
        long buyerUserId)
    {
        var building = await _db.VillageBuildings.FirstOrDefaultAsync(x => x.Id == buildingId && x.PlanetId == planetId);
        if (building is null)
            return new TaskResult(false, "Building not found.");

        var check = ValidateSale(building.ForSale, building.OwnerMemberId, buyerMemberId);
        if (!check.Success)
            return check;

        if (string.IsNullOrWhiteSpace(building.SaleId))
        {
            building.SaleId = CreateSaleId();
            await _db.SaveChangesAsync();
        }

        var payment = await SettlePaymentAsync(
            "building", building.Id, building.SaleId, planetId, building.Price, building.OwnerMemberId, buyerMemberId, buyerUserId, building.Name);

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

    internal static bool CanManageListing(long? ownerMemberId, long actorMemberId, bool canManageVillage) =>
        canManageVillage || ownerMemberId == actorMemberId;

    /// <summary>
    /// Moves the money, or confirms it has already moved. Returns success for a
    /// free listing without touching the ledger at all - a zero-value transfer
    /// would be rejected by the economy and is not worth recording.
    /// </summary>
    private async Task<TaskResult> SettlePaymentAsync(
        string kind,
        long assetId,
        string saleId,
        long planetId,
        decimal price,
        long? sellerMemberId,
        long buyerMemberId,
        long buyerUserId,
        string assetName)
    {
        if (price <= 0)
            return TaskResult.SuccessResult;

        var fingerprint = BuildFingerprint(kind, assetId, saleId, buyerMemberId, sellerMemberId);

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

    private static SemaphoreSlim GetAssetLock(string kind, long planetId, long assetId) =>
        AssetLocks.GetOrAdd($"{planetId}:{kind}:{assetId}", _ => new SemaphoreSlim(1, 1));

    private static string CreateSaleId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Unowned property is sold by the planet itself, so the proceeds go to a
    /// shared account rather than vanishing.
    /// </summary>
    private async Task<EcoAccount?> ResolveSellerAccountAsync(long planetId, long? sellerMemberId)
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

        return shared?.ToModel();
    }
}
