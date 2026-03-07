using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

    public AutomodService(
        ValourDb db,
        ILogger<AutomodService> logger,
        CoreHubService coreHub,
        IServiceProvider serviceProvider,
        PlanetPermissionService permissionService,
        ModerationAuditService moderationAuditService)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
        _moderationAuditService = moderationAuditService;
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
        var take = Math.Min(50, request.Take);
        var skip = request.Skip;
        var query = _db.AutomodTriggers.Where(x => x.PlanetId == planetId).AsQueryable();
        var total = await query.CountAsync();
        var items = await query.Skip(skip).Take(take).Select(x => x.ToModel()).ToListAsync();
        return new QueryResponse<AutomodTrigger>
        {
            Items = items,
            TotalCount = total
        };
    }

    public async Task<QueryResponse<AutomodAction>> QueryTriggerActionsAsync(Guid triggerId, QueryRequest request)
    {
        var take = Math.Min(50, request.Take);
        var skip = request.Skip;
        var query = _db.AutomodActions.Where(x => x.TriggerId == triggerId).AsQueryable();
        var total = await query.CountAsync();
        var items = await query.Skip(skip).Take(take).Select(x => x.ToModel()).ToListAsync();
        return new QueryResponse<AutomodAction>
        {
            Items = items,
            TotalCount = total
        };
    }

    public async Task<TaskResult<AutomodTrigger>> CreateTriggerAsync(AutomodTrigger trigger)
    {
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
        _triggerCache.TryRemove(trigger.PlanetId, out _);

        _coreHub.NotifyPlanetItemCreate(trigger);
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
        _triggerCache.TryRemove(trigger.PlanetId, out _);
        _actionCache.TryRemove(trigger.Id, out _);

        _coreHub.NotifyPlanetItemCreate(trigger);
        foreach (var action in actions)
            _coreHub.NotifyPlanetItemCreate(action.PlanetId, action);

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
        _triggerCache.TryRemove(trigger.PlanetId, out _);

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
        _triggerCache.TryRemove(trigger.PlanetId, out _);
        _actionCache.TryRemove(trigger.Id, out _);

        _coreHub.NotifyPlanetItemDelete(trigger);
        return new(true, "Success");
    }

    public async Task<TaskResult<AutomodAction>> CreateActionAsync(AutomodAction action)
    {
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
        _actionCache.TryRemove(action.TriggerId, out _);

        _coreHub.NotifyPlanetItemCreate(action.PlanetId, action);
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
        _actionCache.TryRemove(action.TriggerId, out _);

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
        _actionCache.TryRemove(action.TriggerId, out _);

        _coreHub.NotifyPlanetItemDelete(action.PlanetId, action);
        return new(true, "Success");
    }

    private readonly ConcurrentDictionary<long, List<AutomodTrigger>> _triggerCache = new();
    private readonly ConcurrentDictionary<Guid, List<AutomodAction>> _actionCache = new();

    private async Task<List<AutomodTrigger>> GetCachedTriggersAsync(long planetId)
    {
        if (_triggerCache.TryGetValue(planetId, out var cached))
            return cached;

        var triggers = await _db.AutomodTriggers.Where(x => x.PlanetId == planetId)
            .Select(x => x.ToModel()).ToListAsync();
        _triggerCache[planetId] = triggers;
        return triggers;
    }

    private async Task<List<AutomodAction>> GetCachedActionsAsync(Guid triggerId)
    {
        if (_actionCache.TryGetValue(triggerId, out var cached))
            return cached;

        var actions = await _db.AutomodActions.Where(x => x.TriggerId == triggerId)
            .Select(x => x.ToModel()).ToListAsync();
        _actionCache[triggerId] = actions;
        return actions;
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
                            var issuerMember = await memberService.GetAsync(action.MemberAddedBy);
                            if (issuerMember is null)
                            {
                                _logger.LogWarning(
                                    "Automod ban action {ActionId} failed: issuer member {IssuerMemberId} not found",
                                    action.Id,
                                    action.MemberAddedBy);
                                break;
                            }

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
                            MentionsData = JsonSerializer.Serialize(new List<Mention>()
                            {
                                new Mention(){ TargetId = targetMemberId, Type = MentionType.PlanetMember}
                            })
                        };

                        var responseResult = await messageService.PostMessageAsync(response);
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

    public async Task<MessageScanResult> ScanMessageAsync(Message message, PlanetMember member)
    {
        if (message.PlanetId is null || member is null)
            return new MessageScanResult { AllowMessage = true }; // DMs are exempt
        
        if (message.AuthorUserId == ISharedUser.VictorUserId)
            return new MessageScanResult { AllowMessage = true }; // Don't scan messages from Victor -- this would create an infinite loop

        var triggers = await GetCachedTriggersAsync(member.PlanetId);
        var hasBypassPermission = await _permissionService.HasPlanetPermissionAsync(member, PlanetPermissions.BypassAutomod);
        if (hasBypassPermission)
            triggers = triggers.Where(t => t.RunForEveryone).ToList();

        if (triggers.Count == 0)
            return new MessageScanResult { AllowMessage = true };

        var recent = await _serviceProvider.GetRequiredService<ChatCacheService>().GetLastMessagesAsync(message.ChannelId);
        var matchedTriggers = triggers
            .Where(t => t.Type != AutomodTriggerType.Join && CheckTrigger(t, message, recent))
            .ToList();

        if (matchedTriggers.Count == 0)
            return new MessageScanResult { AllowMessage = true };

        var matchedTriggerIds = matchedTriggers.Select(t => t.Id).ToList();
        var actionsByTrigger = new Dictionary<Guid, List<AutomodAction>>(matchedTriggers.Count);
        foreach (var trigger in matchedTriggers)
        {
            actionsByTrigger[trigger.Id] = await GetCachedActionsAsync(trigger.Id);
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
            actionsByTrigger[trigger.Id] = await GetCachedActionsAsync(trigger.Id);
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
