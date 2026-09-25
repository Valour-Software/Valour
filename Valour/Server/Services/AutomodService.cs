using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Queries;

namespace Valour.Server.Services;

public class AutomodService
{
    private readonly ValourDb _db;
    private readonly ILogger<AutomodService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly IServiceProvider _serviceProvider;
    private readonly PlanetPermissionService _permissionService;
    private readonly ModerationAuditService _moderationAuditService;
    private readonly HostedPlanetService _hostedPlanetService;

    public AutomodService(
        ValourDb db,
        ILogger<AutomodService> logger,
        CoreHubService coreHub,
        IServiceProvider serviceProvider,
        PlanetPermissionService permissionService,
        ModerationAuditService moderationAuditService,
        HostedPlanetService hostedPlanetService)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
        _moderationAuditService = moderationAuditService;
        _hostedPlanetService = hostedPlanetService;
    }

    /// <summary>
    /// Returns the planet permission a member needs to configure an action of the given type,
    /// or null if the action type does not act on members.
    /// </summary>
    private static PlanetPermission? GetRequiredPermission(AutomodActionType actionType) => actionType switch
    {
        AutomodActionType.Kick => PlanetPermissions.Kick,
        AutomodActionType.Ban => PlanetPermissions.Ban,
        AutomodActionType.AddRole or AutomodActionType.RemoveRole => PlanetPermissions.ManageRoles,
        _ => null
    };

    /// <summary>
    /// Checks that the acting member may configure the given action. Actions run later with
    /// the authority of the member who configured them, so the same rules apply as when a
    /// member performs the action by hand: they must hold the matching permission, and roles
    /// must belong to the planet, must not be admin roles, and must be below the actor's authority.
    /// </summary>
    public async Task<TaskResult> ValidateActionAsync(AutomodAction action, PlanetMember actor)
    {
        if (action is null)
            return TaskResult.FromFailure("Include action.");

        if (actor is null || actor.PlanetId != action.PlanetId)
            return TaskResult.FromFailure("You are not a member of this planet.");

        var requiredPermission = GetRequiredPermission(action.ActionType);
        if (requiredPermission is not null &&
            !await _permissionService.HasPlanetPermissionAsync(actor, requiredPermission))
        {
            return TaskResult.FromFailure($"You need the {requiredPermission.Name} permission to configure this action.");
        }

        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(action.PlanetId);

        if (action.ActionType is AutomodActionType.AddRole or AutomodActionType.RemoveRole)
        {
            if (action.RoleId is null)
                return TaskResult.FromFailure("Select a role for this action.");

            var role = hostedPlanet.GetRoleById(action.RoleId.Value);
            if (role is null || role.PlanetId != action.PlanetId)
                return TaskResult.FromFailure("Role not found in this planet.");

            if (role.IsAdmin)
                return TaskResult.FromFailure("Automod cannot add or remove admin roles.");

            if (role.GetAuthority() >= await _permissionService.GetAuthorityAsync(actor))
                return TaskResult.FromFailure("You can only use roles with a lower authority than your own.");
        }

        if (action.ActionType == AutomodActionType.Respond && action.ResponseChannelId is not null)
        {
            var channel = hostedPlanet.GetChannel(action.ResponseChannelId.Value);
            if (channel is null || channel.ChannelType != ChannelTypeEnum.PlanetChat)
                return TaskResult.FromFailure("Response channel not found in this planet.");
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Automod items run with the authority of the members who configured them. A member may
    /// only change or remove items configured by members who do not outrank them.
    /// </summary>
    public async Task<TaskResult> CanModifyAsync(PlanetMember actor, IEnumerable<long> creatorMemberIds)
    {
        var actorAuthority = await _permissionService.GetAuthorityAsync(actor);
        foreach (var creatorId in creatorMemberIds.Distinct())
        {
            if (creatorId == actor.Id)
                continue;

            var creator = (await _db.PlanetMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == creatorId))?.ToModel();
            if (creator is null || creator.PlanetId != actor.PlanetId)
                continue;

            if (await _permissionService.GetAuthorityAsync(creator) > actorAuthority)
                return TaskResult.FromFailure("This was configured by a member with higher authority than you.");
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Returns the members who configured the trigger and each of its actions.
    /// </summary>
    public async Task<List<long>> GetTriggerCreatorIdsAsync(AutomodTrigger trigger)
    {
        var creators = await _db.AutomodActions.AsNoTracking()
            .Where(x => x.TriggerId == trigger.Id && x.PlanetId == trigger.PlanetId)
            .Select(x => x.MemberAddedBy)
            .ToListAsync();
        creators.Add(trigger.MemberAddedBy);
        return creators;
    }

    public async Task<AutomodTrigger?> GetTriggerAsync(Guid id) =>
        (await _db.AutomodTriggers.FindAsync(id))?.ToModel();

    public async Task<AutomodAction?> GetActionAsync(Guid id) =>
        (await _db.AutomodActions.FindAsync(id))?.ToModel();

    public async Task<List<AutomodTrigger>> GetPlanetTriggersAsync(long planetId) =>
        await _db.AutomodTriggers.Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel()).ToListAsync();

    public async Task<QueryResponse<AutomodTrigger>> QueryPlanetTriggersAsync(long planetId, QueryRequest request)
    {
        var take = Math.Clamp(request.Take, 0, 50);
        var skip = Math.Max(0, request.Skip);
        var query = _db.AutomodTriggers.Where(x => x.PlanetId == planetId).AsQueryable();
        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Id).Skip(skip).Take(take).Select(x => x.ToModel()).ToListAsync();
        return new QueryResponse<AutomodTrigger>
        {
            Items = items,
            TotalCount = total
        };
    }

    public async Task<QueryResponse<AutomodAction>> QueryTriggerActionsAsync(long planetId, Guid triggerId, QueryRequest request)
    {
        var take = Math.Clamp(request.Take, 0, 50);
        var skip = Math.Max(0, request.Skip);
        var query = _db.AutomodActions
            .Where(x => x.PlanetId == planetId && x.TriggerId == triggerId)
            .AsQueryable();
        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Id).Skip(skip).Take(take).Select(x => x.ToModel()).ToListAsync();
        return new QueryResponse<AutomodAction>
        {
            Items = items,
            TotalCount = total
        };
    }

    public async Task<TaskResult<AutomodTrigger>> CreateTriggerAsync(AutomodTrigger trigger)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, trigger.PlanetId);
        if (!migrationGuard.Success)
            return TaskResult<AutomodTrigger>.FromFailure(migrationGuard.Message);

        trigger.Id = Guid.NewGuid();
        try
        {
            await _db.AutomodTriggers.AddAsync(trigger.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        // Invalidate cache
        InvalidateRulesCache(trigger.PlanetId);

        // Moderators' clients hash the new trigger for encrypted channels.
        await _serviceProvider.GetRequiredService<E2eeAutomodService>().RequestWorkAsync(trigger.PlanetId);

        _coreHub.NotifyPlanetItemChange(trigger);
        return new(true, "Success", trigger);
    }

    public async Task<TaskResult<AutomodTrigger>> CreateTriggerWithActionsAsync(AutomodTrigger trigger, List<AutomodAction> actions)
    {
        trigger.Id = Guid.NewGuid();
        foreach (var action in actions)
        {
            action.Id = Guid.NewGuid();
            action.TriggerId = trigger.Id;
        }

        await using var tran = await _db.Database.BeginTransactionAsync();
        try
        {
            await _db.AutomodTriggers.AddAsync(trigger.ToDatabase());
            await _db.SaveChangesAsync();

            if (actions.Count > 0)
                await _db.AutomodActions.AddRangeAsync(actions.Select(x => x.ToDatabase()));
            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        // Invalidate cache
        InvalidateRulesCache(trigger.PlanetId);
        await _serviceProvider.GetRequiredService<E2eeAutomodService>().RequestWorkAsync(trigger.PlanetId);

        _coreHub.NotifyPlanetItemChange(trigger);
        foreach (var action in actions)
            _coreHub.NotifyPlanetItemChange(action.PlanetId, action);

        return new(true, "Success", trigger);
    }

    public async Task<TaskResult<AutomodTrigger>> UpdateTriggerAsync(AutomodTrigger trigger)
    {
        var existing = await _db.AutomodTriggers.FindAsync(trigger.Id);
        if (existing is null)
            return new(false, "Automod trigger not found");

        if (existing.PlanetId != trigger.PlanetId)
            return new(false, "PlanetId cannot be changed.");

        if (existing.MemberAddedBy != trigger.MemberAddedBy)
            return new(false, "MemberAddedBy cannot be changed.");

        var matchingChanged = existing.Type != trigger.Type || existing.TriggerWords != trigger.TriggerWords;

        try
        {
            _db.Entry(existing).CurrentValues.SetValues(trigger.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        // Invalidate cache
        InvalidateRulesCache(trigger.PlanetId);

        if (matchingChanged)
            await _serviceProvider.GetRequiredService<E2eeAutomodService>()
                .InvalidateTriggerAsync(trigger.Id, trigger.PlanetId);

        _coreHub.NotifyPlanetItemChange(trigger);
        return new(true, "Success", trigger);
    }

    public async Task<TaskResult> DeleteTriggerAsync(AutomodTrigger trigger)
    {
        try
        {
            var dbItem = await _db.AutomodTriggers.FindAsync(trigger.Id);
            if (dbItem != null)
            {
                // Delete logs first to satisfy installations that enforce FK(trigger_id -> automod_triggers.id)
                var logs = await _db.AutomodLogs.Where(x => x.TriggerId == trigger.Id).ToListAsync();
                if (logs.Count > 0)
                    _db.AutomodLogs.RemoveRange(logs);

                // Cascade delete actions
                var actions = await _db.AutomodActions.Where(x => x.TriggerId == trigger.Id).ToListAsync();
                if (actions.Count > 0)
                    _db.AutomodActions.RemoveRange(actions);

                _db.AutomodTriggers.Remove(dbItem);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        // Invalidate cache
        InvalidateRulesCache(trigger.PlanetId);
        await _serviceProvider.GetRequiredService<E2eeAutomodService>()
            .InvalidateTriggerAsync(trigger.Id, trigger.PlanetId);

        _coreHub.NotifyPlanetItemDelete(trigger);
        return new(true, "Success");
    }

    public async Task<TaskResult<AutomodAction>> CreateActionAsync(AutomodAction action)
    {
        var migrationGuard = await MigrationLock.GuardAsync(_db, action.PlanetId);
        if (!migrationGuard.Success)
            return TaskResult<AutomodAction>.FromFailure(migrationGuard.Message);

        var trigger = await _db.AutomodTriggers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == action.TriggerId);
        if (trigger is null || trigger.PlanetId != action.PlanetId)
            return TaskResult<AutomodAction>.FromFailure("Automod trigger not found in this planet.");

        action.Id = Guid.NewGuid();
        try
        {
            await _db.AutomodActions.AddAsync(action.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        // Invalidate cache
        InvalidateRulesCache(action.PlanetId);

        _coreHub.NotifyPlanetItemChange(action.PlanetId, action);
        return new(true, "Success", action);
    }

    public async Task<TaskResult<AutomodAction>> UpdateActionAsync(AutomodAction action)
    {
        var existing = await _db.AutomodActions.FindAsync(action.Id);
        if (existing is null)
            return new(false, "Automod action not found");

        if (existing.PlanetId != action.PlanetId)
            return new(false, "PlanetId cannot be changed.");

        if (existing.TriggerId != action.TriggerId)
            return new(false, "TriggerId cannot be changed.");

        if (existing.MemberAddedBy != action.MemberAddedBy)
            return new(false, "MemberAddedBy cannot be changed.");

        try
        {
            _db.Entry(existing).CurrentValues.SetValues(action.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        // Invalidate cache
        InvalidateRulesCache(action.PlanetId);

        _coreHub.NotifyPlanetItemChange(action.PlanetId, action);
        return new(true, "Success", action);
    }

    public async Task<TaskResult> DeleteActionAsync(AutomodAction action)
    {
        try
        {
            var dbItem = await _db.AutomodActions.FindAsync(action.Id);
            if (dbItem != null)
            {
                _db.AutomodActions.Remove(dbItem);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        // Invalidate cache
        InvalidateRulesCache(action.PlanetId);

        _coreHub.NotifyPlanetItemDelete(action.PlanetId, action);
        return new(true, "Success");
    }

    /// <summary>
    /// A planet's automod triggers and their actions, shared by every request on this node.
    /// Callers must treat the lists and models as read-only.
    /// </summary>
    private sealed class PlanetRules
    {
        public required List<AutomodTrigger> Triggers { get; init; }
        public required Dictionary<Guid, List<AutomodAction>> ActionsByTrigger { get; init; }
        public required long ExpiresAt { get; init; }
    }

    /// <summary>
    /// How long cached rules are trusted. Changes made through this service invalidate the
    /// cache immediately on the node that made them. Planet automod routes run on the planet's
    /// hosting node, so this expiry bounds staleness only for changes made elsewhere, such as
    /// account deletion or planet import on another node.
    /// </summary>
    private static readonly TimeSpan RulesCacheLifetime = TimeSpan.FromSeconds(60);

    private static readonly ConcurrentDictionary<long, PlanetRules> RulesCache = new();

    // Incremented on every invalidation. A load that started before an invalidation does
    // not store its possibly stale result.
    private static long _rulesCacheGeneration;

    /// <summary>
    /// Drops the cached automod rules for a planet on this node.
    /// </summary>
    public static void InvalidateRulesCache(long planetId)
    {
        Interlocked.Increment(ref _rulesCacheGeneration);
        RulesCache.TryRemove(planetId, out _);
    }

    /// <summary>
    /// Drops all cached automod rules on this node.
    /// </summary>
    public static void InvalidateAllRulesCaches()
    {
        Interlocked.Increment(ref _rulesCacheGeneration);
        RulesCache.Clear();
    }

    private async Task<PlanetRules> GetCachedRulesAsync(long planetId)
    {
        var now = Environment.TickCount64;
        if (RulesCache.TryGetValue(planetId, out var cached) && cached.ExpiresAt > now)
            return cached;

        var generation = Interlocked.Read(ref _rulesCacheGeneration);

        var triggers = await _db.AutomodTriggers.AsNoTracking()
            .Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel()).ToListAsync();

        var actionsByTrigger = new Dictionary<Guid, List<AutomodAction>>();
        if (triggers.Count > 0)
        {
            // Only actions from the trigger's own planet may run for it
            var actions = await _db.AutomodActions.AsNoTracking()
                .Where(x => x.PlanetId == planetId)
                .Select(x => x.ToModel()).ToListAsync();

            foreach (var action in actions)
            {
                if (!actionsByTrigger.TryGetValue(action.TriggerId, out var list))
                {
                    list = new List<AutomodAction>();
                    actionsByTrigger[action.TriggerId] = list;
                }

                list.Add(action);
            }
        }

        var rules = new PlanetRules
        {
            Triggers = triggers,
            ActionsByTrigger = actionsByTrigger,
            ExpiresAt = Environment.TickCount64 + (long)RulesCacheLifetime.TotalMilliseconds
        };

        if (Interlocked.Read(ref _rulesCacheGeneration) == generation)
            RulesCache[planetId] = rules;

        return rules;
    }

    private async Task<List<AutomodTrigger>> GetCachedTriggersAsync(long planetId) =>
        (await GetCachedRulesAsync(planetId)).Triggers;

    private async Task<List<AutomodAction>> GetCachedActionsAsync(AutomodTrigger trigger)
    {
        var rules = await GetCachedRulesAsync(trigger.PlanetId);
        return rules.ActionsByTrigger.TryGetValue(trigger.Id, out var actions)
            ? actions
            : new List<AutomodAction>();
    }

    private static bool IsMessageBlockAction(AutomodActionType actionType) =>
        actionType is AutomodActionType.DeleteMessage or AutomodActionType.BlockMessage;

    public sealed class MessageScanResult
    {
        public bool AllowMessage { get; init; }
        public List<AutomodAction> ActionsToRun { get; init; } = new();
        public List<AutomodAction> BlockingActions { get; init; } = new();
    }

    public async Task RunMessageActionsAsync(IEnumerable<AutomodAction> actions, PlanetMember member, Message message)
    {
        await RunActionsAsync(actions, member, message);
    }

    private static ModerationActionType ToAuditActionType(AutomodActionType actionType) => actionType switch
    {
        AutomodActionType.Kick => ModerationActionType.Kick,
        AutomodActionType.Ban => ModerationActionType.Ban,
        AutomodActionType.AddRole => ModerationActionType.AddRole,
        AutomodActionType.RemoveRole => ModerationActionType.RemoveRole,
        AutomodActionType.DeleteMessage => ModerationActionType.DeleteMessage,
        AutomodActionType.BlockMessage => ModerationActionType.BlockMessage,
        _ => ModerationActionType.Respond
    };

    /// <summary>
    /// Returns the member who configured a member-targeted action if they may still apply it
    /// to the target, or null if the action must be skipped. The owner and admins are never
    /// targeted, and the configuring member must still be in the planet, still hold the
    /// matching permission, and still outrank the target. Role actions additionally require
    /// a non-admin role of this planet that is below the configuring member's authority.
    /// </summary>
    private async Task<PlanetMember?> GetAuthorizedIssuerAsync(
        AutomodAction action,
        PlanetMember target,
        PlanetMemberService memberService)
    {
        var hostedPlanet = await _hostedPlanetService.GetRequiredAsync(target.PlanetId);
        string? skipReason = null;
        PlanetMember? issuer = null;

        if (action.PlanetId != target.PlanetId)
        {
            skipReason = "action belongs to another planet";
        }
        else if (hostedPlanet.Planet.OwnerId == target.UserId || await _permissionService.IsAdminAsync(target))
        {
            skipReason = "target is the planet owner or an admin";
        }
        else
        {
            issuer = await memberService.GetAsync(action.MemberAddedBy);
            var requiredPermission = GetRequiredPermission(action.ActionType);

            if (issuer is null || issuer.PlanetId != target.PlanetId)
            {
                skipReason = "configuring member is no longer in the planet";
            }
            else if (requiredPermission is not null &&
                     !await _permissionService.HasPlanetPermissionAsync(issuer, requiredPermission))
            {
                skipReason = "configuring member no longer has permission";
            }
            else
            {
                var issuerAuthority = await _permissionService.GetAuthorityAsync(issuer);
                if (issuerAuthority <= await _permissionService.GetAuthorityAsync(target))
                {
                    skipReason = "configuring member does not outrank the target";
                }
                else if (action.ActionType is AutomodActionType.AddRole or AutomodActionType.RemoveRole)
                {
                    var role = action.RoleId is null ? null : hostedPlanet.GetRoleById(action.RoleId.Value);
                    if (role is null || role.IsAdmin || role.GetAuthority() >= issuerAuthority)
                        skipReason = "role is missing, an admin role, or not below the configuring member";
                }
            }
        }

        if (skipReason is null)
            return issuer;

        _logger.LogWarning(
            "Automod {ActionType} action {ActionId} skipped for member {MemberId}: {Reason}",
            action.ActionType, action.Id, target.Id, skipReason);
        return null;
    }

    private async Task RunActionsAsync(IEnumerable<AutomodAction> actions, PlanetMember member, Message? message)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var memberService = scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        var banService = scope.ServiceProvider.GetRequiredService<PlanetBanService>();
        var messageService = scope.ServiceProvider.GetRequiredService<MessageService>();
        var planetService = scope.ServiceProvider.GetRequiredService<PlanetService>();

        foreach (var action in actions)
        {
            try
            {
                switch (action.ActionType)
                {
                    case AutomodActionType.Kick:
                        {
                            if (await GetAuthorizedIssuerAsync(action, member, memberService) is null)
                                break;

                            var kickResult = await memberService.DeleteAsync(member.Id);
                            if (!kickResult.Success)
                            {
                                _logger.LogWarning(
                                    "Automod kick action {ActionId} failed for member {MemberId}: {Reason}",
                                    action.Id,
                                    member.Id,
                                    kickResult.Message);
                            }
                            else
                            {
                                await _moderationAuditService.LogAsync(
                                    member.PlanetId,
                                    ModerationActionSource.Automod,
                                    ModerationActionType.Kick,
                                    actorUserId: ISharedUser.VictorUserId,
                                    targetUserId: member.UserId,
                                    targetMemberId: member.Id,
                                    messageId: message?.Id,
                                    triggerId: action.TriggerId,
                                    details: action.Message);
                            }
                            break;
                        }
                    case AutomodActionType.Ban:
                        {
                            var issuerMember = await GetAuthorizedIssuerAsync(action, member, memberService);
                            if (issuerMember is null)
                                break;

                            var ban = new PlanetBan
                            {
                                Id = IdManager.Generate(),
                                PlanetId = member.PlanetId,
                                TargetId = member.UserId,
                                IssuerId = issuerMember.UserId,
                                Reason = action.Message,
                                TimeCreated = DateTime.UtcNow,
                                TimeExpires = action.Expires
                            };

                            var banResult = await banService.CreateAsync(
                                ban,
                                issuerMember,
                                ModerationActionSource.Automod,
                                action.TriggerId);
                            if (!banResult.Success)
                            {
                                _logger.LogWarning(
                                    "Automod ban action {ActionId} failed for member {MemberId}: {Reason}",
                                    action.Id,
                                    member.Id,
                                    banResult.Message);
                            }
                            break;
                        }
                    case AutomodActionType.AddRole:
                        {
                            if (!action.RoleId.HasValue)
                            {
                                _logger.LogWarning("Automod add role action {ActionId} skipped: RoleId missing", action.Id);
                                break;
                            }

                            if (await GetAuthorizedIssuerAsync(action, member, memberService) is null)
                                break;

                            var addRoleResult = await memberService.AddRoleAsync(member.PlanetId, member.Id, action.RoleId.Value);
                            if (!addRoleResult.Success)
                            {
                                _logger.LogWarning(
                                    "Automod add role action {ActionId} failed for member {MemberId}: {Reason}",
                                    action.Id,
                                    member.Id,
                                    addRoleResult.Message);
                            }
                            else
                            {
                                await _moderationAuditService.LogAsync(
                                    member.PlanetId,
                                    ModerationActionSource.Automod,
                                    ModerationActionType.AddRole,
                                    actorUserId: ISharedUser.VictorUserId,
                                    targetUserId: member.UserId,
                                    targetMemberId: member.Id,
                                    messageId: message?.Id,
                                    triggerId: action.TriggerId,
                                    details: $"RoleId={action.RoleId.Value}");
                            }
                            break;
                        }
                    case AutomodActionType.RemoveRole:
                        {
                            if (!action.RoleId.HasValue)
                            {
                                _logger.LogWarning("Automod remove role action {ActionId} skipped: RoleId missing", action.Id);
                                break;
                            }

                            if (await GetAuthorizedIssuerAsync(action, member, memberService) is null)
                                break;

                            var removeRoleResult = await memberService.RemoveRoleAsync(member.PlanetId, member.Id, action.RoleId.Value);
                            if (!removeRoleResult.Success)
                            {
                                _logger.LogWarning(
                                    "Automod remove role action {ActionId} failed for member {MemberId}: {Reason}",
                                    action.Id,
                                    member.Id,
                                    removeRoleResult.Message);
                            }
                            else
                            {
                                await _moderationAuditService.LogAsync(
                                    member.PlanetId,
                                    ModerationActionSource.Automod,
                                    ModerationActionType.RemoveRole,
                                    actorUserId: ISharedUser.VictorUserId,
                                    targetUserId: member.UserId,
                                    targetMemberId: member.Id,
                                    messageId: message?.Id,
                                    triggerId: action.TriggerId,
                                    details: $"RoleId={action.RoleId.Value}");
                            }
                            break;
                        }
                    case AutomodActionType.DeleteMessage:
                    case AutomodActionType.BlockMessage:
                        // Message-level blocking is decided before posting in ScanMessageAsync.
                        break;
                    case AutomodActionType.Respond:
                        long targetMemberId;
                        long targetChannelId;
                        long targetPlanetId;

                        if (action.ResponseChannelId.HasValue)
                        {
                            targetChannelId = action.ResponseChannelId.Value;
                            targetMemberId = message?.AuthorMemberId ?? member.Id;
                            targetPlanetId = message?.PlanetId ?? member.PlanetId;

                            if (message is not null && message.AuthorMemberId is null)
                                break;
                        }
                        else if (message is not null)
                        {
                            if (message.AuthorMemberId is null)
                                break;

                            targetMemberId = message.AuthorMemberId.Value;
                            targetChannelId = message.ChannelId;
                            targetPlanetId = message.PlanetId ?? member.PlanetId;
                        }
                        else
                        {
                            var defaultChannel = await planetService.GetPrimaryChannelAsync(member.PlanetId);
                            if (defaultChannel is null)
                            {
                                _logger.LogWarning(
                                    "Automod respond action {ActionId} could not find default channel for planet {PlanetId}",
                                    action.Id, member.PlanetId);
                                break;
                            }

                            targetMemberId = member.Id;
                            targetChannelId = defaultChannel.Id;
                            targetPlanetId = member.PlanetId;
                        }

                        var response = new Message
                        {
                            Id = IdManager.Generate(),
                            ChannelId = targetChannelId,
                            AuthorMemberId = null,
                            AuthorUserId = ISharedUser.VictorUserId,
                            Content = $"«@m-{targetMemberId}» {action.Message ?? string.Empty}",
                            TimeSent = DateTime.UtcNow,
                            PlanetId = targetPlanetId,
                            Fingerprint = Guid.NewGuid().ToString(),
                            Mentions =
                            [
                                new Mention() { TargetId = targetMemberId, Type = MentionType.PlanetMember }
                            ]
                        };

                        // Responses are posted by Victor, who has no member to check MentionAll
                        // against, so role mentions in the configured text never notify anyone.
                        var responseResult = await messageService.PostMessageAsync(response, new MessageWriteOptions
                        {
                            SuppressRoleMentions = true,
                            SealKind = Valour.Sdk.E2ee.ServerSealedKind.System
                        });
                        if (!responseResult.Success)
                        {
                            _logger.LogWarning(
                                "Automod respond action {ActionId} failed to post message in planet {PlanetId}: {Reason}",
                                action.Id, targetPlanetId, responseResult.Message);
                        }
                        else
                        {
                            await _moderationAuditService.LogAsync(
                                member.PlanetId,
                                ModerationActionSource.Automod,
                                ModerationActionType.Respond,
                                actorUserId: ISharedUser.VictorUserId,
                                targetUserId: member.UserId,
                                targetMemberId: member.Id,
                                messageId: responseResult.Data?.Id ?? message?.Id,
                                triggerId: action.TriggerId,
                                details: action.Message);
                        }
                        break;
                    }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Automod action {ActionId} ({ActionType}) failed for member {MemberId}",
                    action.Id,
                    action.ActionType,
                    member.Id);
            }
        }
    }

    private static List<AutomodAction> FilterActionsByStrikes(IEnumerable<AutomodAction> actions, int globalCount, int triggerCount)
    {
        return actions.Where(a =>
                a.Strikes <= 1 ||
                (a.UseGlobalStrikes ? globalCount >= a.Strikes : triggerCount >= a.Strikes))
            .ToList();
    }

    private static bool CheckTrigger(AutomodTrigger trigger, Message message, IList<Message> recentMessages)
    {
        switch (trigger.Type)
        {
            case AutomodTriggerType.Blacklist:
                if (string.IsNullOrWhiteSpace(trigger.TriggerWords))
                    return false;
                foreach (var word in trigger.TriggerWords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (message.Content?.Contains(word, StringComparison.OrdinalIgnoreCase) == true)
                        return true;
                }
                break;
            case AutomodTriggerType.Command:
                if (string.IsNullOrWhiteSpace(trigger.TriggerWords) || string.IsNullOrWhiteSpace(message.Content))
                    return false;
                var trimmed = message.Content.Trim();
                if (trimmed.StartsWith("/" + trigger.TriggerWords, StringComparison.OrdinalIgnoreCase))
                    return true;
                break;
            case AutomodTriggerType.Spam:
                if (recentMessages is null)
                    return false;

                var messageThreshold = 5;
                var windowSeconds = 10;

                if (!string.IsNullOrWhiteSpace(trigger.TriggerWords))
                {
                    var parts = trigger.TriggerWords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1 && int.TryParse(parts[0], out var configuredThreshold))
                        messageThreshold = Math.Clamp(configuredThreshold, 2, 50);

                    if (parts.Length >= 2 && int.TryParse(parts[1], out var configuredWindow))
                        windowSeconds = Math.Clamp(configuredWindow, 1, 300);
                }

                var now = DateTime.UtcNow;
                var count = recentMessages.Count(m =>
                    m.AuthorMemberId == message.AuthorMemberId &&
                    (now - m.TimeSent).TotalSeconds < windowSeconds);

                if (count >= messageThreshold)
                    return true;
                break;
            case AutomodTriggerType.Join:
                return true;
        }
        return false;
    }

    public async Task<MessageScanResult> ScanMessageAsync(Message message, PlanetMember member, bool isEdit = false)
    {
        if (message.PlanetId is null)
            return new MessageScanResult { AllowMessage = true }; // DMs are exempt

        // Webhook messages are posted as Victor without a member, but carry external content
        if (message.WebhookId is not null)
            return await ScanWebhookMessageAsync(message);

        if (member is null)
            return new MessageScanResult { AllowMessage = true };

        if (message.AuthorUserId == ISharedUser.VictorUserId)
            return new MessageScanResult { AllowMessage = true }; // Don't scan messages from Victor -- this would create an infinite loop

        var triggers = await GetCachedTriggersAsync(member.PlanetId);
        var hasBypassPermission = await _permissionService.HasPlanetPermissionAsync(member, PlanetPermissions.BypassAutomod);
        if (hasBypassPermission)
            triggers = triggers.Where(t => t.RunForEveryone).ToList();

        if (triggers.Count == 0)
            return new MessageScanResult { AllowMessage = true };

        var recent = await _serviceProvider.GetRequiredService<ChatCacheService>().GetLastMessagesAsync(message.ChannelId);
        var encryptedAutomod = _serviceProvider.GetRequiredService<E2eeAutomodService>();
        var matchedTriggers = new List<AutomodTrigger>();
        foreach (var trigger in triggers)
        {
            if (trigger.Type == AutomodTriggerType.Join)
                continue;

            // Spam triggers count recent posts, and an edit is not a new post.
            if (isEdit && trigger.Type == AutomodTriggerType.Spam)
                continue;

            // Chat messages are encrypted, so word and command triggers match
            // them through keyed search terms, and text the server sealed
            // itself is never scanned. Only thread posts and comments, which
            // are public and not encrypted, are matched against their text.
            if (message.EncryptionVersion != Valour.Sdk.E2ee.MessageEncryption.None &&
                E2eeAutomodService.UsesTerms(trigger.Type))
            {
                if (message.EncryptionVersion == Valour.Sdk.E2ee.MessageEncryption.EndToEnd &&
                    await encryptedAutomod.MatchesAsync(trigger, message))
                    matchedTriggers.Add(trigger);
                continue;
            }

            if (CheckTrigger(trigger, message, recent))
                matchedTriggers.Add(trigger);
        }

        if (matchedTriggers.Count == 0)
            return new MessageScanResult { AllowMessage = true };

        var matchedTriggerIds = matchedTriggers.Select(t => t.Id).ToList();
        var actionsByTrigger = new Dictionary<Guid, List<AutomodAction>>(matchedTriggers.Count);
        foreach (var trigger in matchedTriggers)
        {
            actionsByTrigger[trigger.Id] = await GetCachedActionsAsync(trigger);
        }

        var now = DateTime.UtcNow;
        var logs = matchedTriggers.Select(trigger => new Valour.Database.AutomodLog
        {
            Id = Guid.NewGuid(),
            PlanetId = member.PlanetId,
            TriggerId = trigger.Id,
            MemberId = member.Id,
            MessageId = message.Id,
            TimeTriggered = now
        }).ToList();

        await _db.AutomodLogs.AddRangeAsync(logs);
        await _db.SaveChangesAsync();

        var globalCount = await _db.AutomodLogs.CountAsync(l => l.PlanetId == member.PlanetId && l.MemberId == member.Id);
        var triggerCounts = await _db.AutomodLogs
            .Where(l => l.MemberId == member.Id && matchedTriggerIds.Contains(l.TriggerId))
            .GroupBy(l => l.TriggerId)
            .Select(g => new { TriggerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TriggerId, x => x.Count);

        var actionsToRun = new List<AutomodAction>();
        var blockingActions = new List<AutomodAction>();
        var allow = true;

        foreach (var trigger in matchedTriggers)
        {
            if (!actionsByTrigger.TryGetValue(trigger.Id, out var actions) || actions.Count == 0)
                continue;

            triggerCounts.TryGetValue(trigger.Id, out var triggerCount);
            var filteredActions = FilterActionsByStrikes(actions, globalCount, triggerCount);
            if (filteredActions.Count == 0)
                continue;

            var triggerBlocking = filteredActions.Where(a => IsMessageBlockAction(a.ActionType)).ToList();
            if (triggerBlocking.Count > 0)
                allow = false;

            blockingActions.AddRange(triggerBlocking);
            actionsToRun.AddRange(filteredActions.Where(a => !IsMessageBlockAction(a.ActionType)));
        }

        if (blockingActions.Count > 0)
        {
            foreach (var action in blockingActions)
            {
                await _moderationAuditService.LogAsync(
                    member.PlanetId,
                    ModerationActionSource.Automod,
                    ToAuditActionType(action.ActionType),
                    actorUserId: ISharedUser.VictorUserId,
                    targetUserId: member.UserId,
                    targetMemberId: member.Id,
                    messageId: message.Id,
                    triggerId: action.TriggerId,
                    details: action.Message);
            }
        }

        return new MessageScanResult
        {
            AllowMessage = allow,
            ActionsToRun = actionsToRun,
            BlockingActions = blockingActions
        };
    }

    /// <summary>
    /// Scans a webhook message. Webhooks have no member, so content triggers apply to every
    /// webhook message, strike counts cannot accumulate (only first-strike actions apply), and
    /// only blocking actions take effect. Member-targeted actions such as kick, ban, and role
    /// changes are never run. Spam triggers count messages per member and do not apply here;
    /// webhook bursts are limited by the webhook rate limit instead.
    /// </summary>
    private async Task<MessageScanResult> ScanWebhookMessageAsync(Message message)
    {
        var planetId = message.PlanetId!.Value;
        var triggers = (await GetCachedTriggersAsync(planetId))
            .Where(t => t.Type is AutomodTriggerType.Blacklist or AutomodTriggerType.Command)
            .ToList();
        if (triggers.Count == 0)
            return new MessageScanResult { AllowMessage = true };

        var blockingActions = new List<AutomodAction>();

        foreach (var trigger in triggers)
        {
            if (!CheckTrigger(trigger, message, null))
                continue;

            var actions = await GetCachedActionsAsync(trigger);
            blockingActions.AddRange(FilterActionsByStrikes(actions, 1, 1)
                .Where(a => IsMessageBlockAction(a.ActionType)));
        }

        foreach (var action in blockingActions)
        {
            await _moderationAuditService.LogAsync(
                planetId,
                ModerationActionSource.Automod,
                ToAuditActionType(action.ActionType),
                actorUserId: ISharedUser.VictorUserId,
                messageId: message.Id,
                triggerId: action.TriggerId,
                details: action.Message);
        }

        return new MessageScanResult
        {
            AllowMessage = blockingActions.Count == 0,
            BlockingActions = blockingActions
        };
    }

    public async Task HandleMemberJoinAsync(PlanetMember member)
    {
        var joinTriggers = (await GetCachedTriggersAsync(member.PlanetId))
            .Where(t => t.Type == AutomodTriggerType.Join)
            .ToList();
        var hasBypassPermission = await _permissionService.HasPlanetPermissionAsync(member, PlanetPermissions.BypassAutomod);
        if (hasBypassPermission)
            joinTriggers = joinTriggers.Where(t => t.RunForEveryone).ToList();

        if (joinTriggers.Count == 0)
            return;

        var joinTriggerIds = joinTriggers.Select(t => t.Id).ToList();
        var actionsByTrigger = new Dictionary<Guid, List<AutomodAction>>(joinTriggers.Count);
        foreach (var trigger in joinTriggers)
        {
            actionsByTrigger[trigger.Id] = await GetCachedActionsAsync(trigger);
        }

        var now = DateTime.UtcNow;
        var logs = joinTriggers.Select(trigger => new Valour.Database.AutomodLog
        {
            Id = Guid.NewGuid(),
            PlanetId = member.PlanetId,
            TriggerId = trigger.Id,
            MemberId = member.Id,
            MessageId = null,
            TimeTriggered = now
        }).ToList();

        await _db.AutomodLogs.AddRangeAsync(logs);
        await _db.SaveChangesAsync();

        var globalCount = await _db.AutomodLogs.CountAsync(l => l.PlanetId == member.PlanetId && l.MemberId == member.Id);
        var triggerCounts = await _db.AutomodLogs
            .Where(l => l.MemberId == member.Id && joinTriggerIds.Contains(l.TriggerId))
            .GroupBy(l => l.TriggerId)
            .Select(g => new { TriggerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TriggerId, x => x.Count);

        foreach (var trigger in joinTriggers)
        {
            if (!actionsByTrigger.TryGetValue(trigger.Id, out var actions) || actions.Count == 0)
                continue;

            triggerCounts.TryGetValue(trigger.Id, out var triggerCount);
            var filteredActions = FilterActionsByStrikes(actions, globalCount, triggerCount)
                .Where(a => !IsMessageBlockAction(a.ActionType))
                .ToList();

            if (filteredActions.Count == 0)
                continue;

            await RunActionsAsync(filteredActions, member, null);
        }
    }
}
