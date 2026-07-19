using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class PlanetRuleService
{
    private readonly ValourDb _db;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly CoreHubService _coreHub;
    private readonly ILogger<PlanetRuleService> _logger;

    public PlanetRuleService(
        ValourDb db,
        HostedPlanetService hostedPlanetService,
        CoreHubService coreHub,
        ILogger<PlanetRuleService> logger)
    {
        _db = db;
        _hostedPlanetService = hostedPlanetService;
        _coreHub = coreHub;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlanetRule>> GetAllAsync(long planetId)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hosted.Rules.List;
    }

    public async Task<PlanetRule> GetAsync(long planetId, long ruleId)
    {
        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        return hosted.GetRule(ruleId);
    }

    public async Task<TaskResult<PlanetRule>> CreateAsync(PlanetRule rule)
    {
        var validation = Validate(rule);
        if (!validation.Success)
            return TaskResult<PlanetRule>.FromFailure(validation.Message);

        var hosted = await _hostedPlanetService.GetRequiredAsync(rule.PlanetId);

        if (hosted.Planet.LockedForMigration)
            return TaskResult<PlanetRule>.FromFailure(MigrationLock.Message);

        rule.Id = IdManager.Generate();
        rule.Position = hosted.Rules.List.Count == 0
            ? 0
            : hosted.Rules.List.Max(x => x.Position) + 1;

        try
        {
            await _db.PlanetRules.AddAsync(rule.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create rule on planet {PlanetId}", rule.PlanetId);
            return TaskResult<PlanetRule>.FromFailure("Failed to create rule.");
        }

        hosted.UpsertRule(rule);
        _coreHub.NotifyPlanetItemChange(rule);

        return TaskResult<PlanetRule>.FromData(rule);
    }

    public async Task<TaskResult<PlanetRule>> UpdateAsync(PlanetRule updatedRule)
    {
        var validation = Validate(updatedRule);
        if (!validation.Success)
            return TaskResult<PlanetRule>.FromFailure(validation.Message);

        var migrationGuard = await MigrationLock.GuardAsync(_db, updatedRule.PlanetId);
        if (!migrationGuard.Success)
            return TaskResult<PlanetRule>.FromFailure(migrationGuard.Message);

        var dbRule = await _db.PlanetRules
            .FirstOrDefaultAsync(x => x.PlanetId == updatedRule.PlanetId && x.Id == updatedRule.Id);

        if (dbRule is null)
            return TaskResult<PlanetRule>.FromFailure("Rule not found.");

        if (updatedRule.Position != dbRule.Position)
            return TaskResult<PlanetRule>.FromFailure("Position cannot be changed directly.");

        try
        {
            _db.Entry(dbRule).CurrentValues.SetValues(updatedRule);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update rule {RuleId} on planet {PlanetId}", updatedRule.Id, updatedRule.PlanetId);
            return TaskResult<PlanetRule>.FromFailure("Failed to update rule.");
        }

        var hosted = await _hostedPlanetService.GetRequiredAsync(updatedRule.PlanetId);
        hosted.UpsertRule(updatedRule);
        _coreHub.NotifyPlanetItemChange(updatedRule);

        return TaskResult<PlanetRule>.FromData(updatedRule);
    }

    public async Task<TaskResult> DeleteAsync(long planetId, long ruleId)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, planetId);
        if (!migrationGuard.Success)
            return migrationGuard;

        var dbRule = await _db.PlanetRules
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.Id == ruleId);

        if (dbRule is null)
            return TaskResult.FromFailure("Rule not found.");

        try
        {
            _db.PlanetRules.Remove(dbRule);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to delete rule {RuleId} on planet {PlanetId}", ruleId, planetId);
            return TaskResult.FromFailure("Failed to delete rule.");
        }

        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        hosted.RemoveRule(ruleId);
        _coreHub.NotifyPlanetItemDelete(dbRule.ToModel());

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> SetRuleOrderAsync(long planetId, long[] order)
    {
        if (order is null || order.Length == 0)
            return TaskResult.FromFailure("Rule order cannot be empty.");

        var dbRules = await _db.PlanetRules
            .Where(x => x.PlanetId == planetId)
            .ToListAsync();

        if (dbRules.Count != order.Length)
            return TaskResult.FromFailure("Rule order must include every planet rule.");

        var ruleById = dbRules.ToDictionary(x => x.Id);
        var seen = new HashSet<long>();
        foreach (var ruleId in order)
        {
            if (!seen.Add(ruleId))
                return TaskResult.FromFailure($"Duplicate rule in order ({ruleId}).");

            if (!ruleById.ContainsKey(ruleId))
                return TaskResult.FromFailure($"Rule {ruleId} does not belong to this planet.");
        }

        var changedRules = new List<PlanetRule>();

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            for (var i = 0; i < order.Length; i++)
            {
                var dbRule = ruleById[order[i]];
                var newPosition = (uint)i;
                if (dbRule.Position == newPosition)
                    continue;

                dbRule.Position = newPosition;
                changedRules.Add(dbRule.ToModel());
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            _logger.LogError(e, "Failed to reorder rules on planet {PlanetId}", planetId);
            return TaskResult.FromFailure("Failed to reorder rules.");
        }

        var hosted = await _hostedPlanetService.GetRequiredAsync(planetId);
        foreach (var rule in changedRules)
        {
            hosted.UpsertRule(rule);
            _coreHub.NotifyPlanetItemChange(rule);
        }

        return TaskResult.SuccessResult;
    }

    private static TaskResult Validate(PlanetRule rule)
    {
        if (rule is null)
            return TaskResult.FromFailure("Rule is required.");

        rule.Title = rule.Title?.Trim();
        rule.Description ??= string.Empty;

        if (string.IsNullOrWhiteSpace(rule.Title))
            return TaskResult.FromFailure("Rule title cannot be empty.");

        if (rule.Title.Length > ISharedPlanetRule.MaxTitleLength)
            return TaskResult.FromFailure($"Rule title must be {ISharedPlanetRule.MaxTitleLength} characters or less.");

        if (rule.Description.Length > ISharedPlanetRule.MaxDescriptionLength)
            return TaskResult.FromFailure($"Rule description must be {ISharedPlanetRule.MaxDescriptionLength} characters or less.");

        return TaskResult.SuccessResult;
    }
}
