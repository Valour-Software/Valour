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

    public AutomodService(
        ValourDb db,
        ILogger<AutomodService> logger,
        CoreHubService coreHub,
        IServiceProvider serviceProvider,
        PlanetPermissionService permissionService)
    {
        _db = db;
        _logger = logger;
        _coreHub = coreHub;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
    }

    public async Task<AutomodTrigger?> GetTriggerAsync(Guid id) =>
        (await _db.AutomodTriggers.FindAsync(id))?.ToModel();

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
                _db.AutomodTriggers.Remove(dbItem);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

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

        _coreHub.NotifyPlanetItemChange(action.PlanetId, action);
        return new(true, "Success", action);
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

    private async Task RunActionsAsync(IEnumerable<AutomodAction> actions, PlanetMember member, Message? message)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var memberService = scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        var banService = scope.ServiceProvider.GetRequiredService<PlanetBanService>();
        var messageService = scope.ServiceProvider.GetRequiredService<MessageService>();

        foreach (var action in actions)
        {
            switch (action.ActionType)
            {
                case AutomodActionType.Kick:
                    await memberService.DeleteAsync(member.Id);
                    break;
                case AutomodActionType.Ban:
                    var ban = new PlanetBan
                    {
                        Id = IdManager.Generate(),
                        PlanetId = member.PlanetId,
                        TargetId = member.UserId,
                        IssuerId = action.MemberAddedBy,
                        Reason = action.Message,
                        TimeCreated = DateTime.UtcNow,
                        TimeExpires = action.Expires
                    };
                    await banService.CreateAsync(ban, member);
                    break;
                case AutomodActionType.AddRole:
                    if (action.RoleId.HasValue)
                        await memberService.AddRoleAsync(member.PlanetId, member.Id, action.RoleId.Value);
                    break;
                case AutomodActionType.RemoveRole:
                    if (action.RoleId.HasValue)
                        await memberService.RemoveRoleAsync(member.PlanetId, member.Id, action.RoleId.Value);
                    break;
                case AutomodActionType.DeleteMessage:
                    if (message is not null)
                        await messageService.DeleteMessageAsync(message.Id);
                    break;
                case AutomodActionType.Respond:
                    if (message is not null && message.AuthorMemberId is not null)
                    {
                        var response = new Message
                        {
                            Id = IdManager.Generate(),
                            ChannelId = message.ChannelId,
                            AuthorMemberId = null,
                            AuthorUserId = ISharedUser.VictorUserId,
                            Content = $"«@m-{message.AuthorMemberId}» "  + action.Message,
                            TimeSent = DateTime.UtcNow,
                            PlanetId = message.PlanetId,
                            Fingerprint = Guid.NewGuid().ToString(),
                            MentionsData = JsonSerializer.Serialize(new List<Mention>()
                            {
                                new Mention(){ TargetId = message.AuthorMemberId.Value, Type = MentionType.PlanetMember}
                            })
                        };

                        await messageService.PostMessageAsync(response);
                    }
                    break;
            }
        }
    }

    private async Task<List<AutomodAction>> FilterActionsByStrikesAsync(IEnumerable<AutomodAction> actions, PlanetMember member, Guid triggerId)
    {
        var globalCount = await _db.AutomodLogs.CountAsync(l => l.PlanetId == member.PlanetId && l.MemberId == member.Id);
        var triggerCount = await _db.AutomodLogs.CountAsync(l => l.TriggerId == triggerId && l.MemberId == member.Id);

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
                var now = DateTime.UtcNow;
                var count = recentMessages.Count(m => m.AuthorMemberId == message.AuthorMemberId && (now - m.TimeSent).TotalSeconds < 10);
                if (count >= 5)
                    return true;
                break;
            case AutomodTriggerType.Join:
                return true;
        }
        return false;
    }

    public async Task<bool> ScanMessageAsync(Message message, PlanetMember member)
    {
        if (message.PlanetId is null || member is null)
            return true; // DMs are exempt
        
        if (message.AuthorUserId == ISharedUser.VictorUserId)
            return true; // Don't scan messages from Victor -- this would create an infinite loop
        
        //if (await _permissionService.HasPlanetPermissionAsync(member, PlanetPermissions.BypassAutomod))
        //    return true;

        var triggers = await GetCachedTriggersAsync(member.PlanetId);
        if (triggers.Count == 0)
            return true;

        var recent = await _serviceProvider.GetRequiredService<ChatCacheService>().GetLastMessagesAsync(message.ChannelId);
        bool allow = true;

        foreach (var trigger in triggers.Where(t => t.Type != AutomodTriggerType.Join))
        {
            if (!CheckTrigger(trigger, message, recent))
                continue;

            var actions = await GetCachedActionsAsync(trigger.Id);

            var log = new Valour.Database.AutomodLog
            {
                Id = Guid.NewGuid(),
                PlanetId = member.PlanetId,
                TriggerId = trigger.Id,
                MemberId = member.Id,
                MessageId = message.Id,
                TimeTriggered = DateTime.UtcNow
            };
            await _db.AutomodLogs.AddAsync(log);
            await _db.SaveChangesAsync();

            actions = await FilterActionsByStrikesAsync(actions, member, trigger.Id);
            await RunActionsAsync(actions, member, message);

            if (actions.Any(a => a.ActionType == AutomodActionType.DeleteMessage))
                allow = false;
        }

        return allow;
    }

    public async Task HandleMemberJoinAsync(PlanetMember member)
    {
        if (await _permissionService.HasPlanetPermissionAsync(member, PlanetPermissions.BypassAutomod))
            return;

        var triggers = await GetCachedTriggersAsync(member.PlanetId);
        foreach (var trigger in triggers.Where(t => t.Type == AutomodTriggerType.Join))
        {
            var actions = await GetCachedActionsAsync(trigger.Id);
            var log = new Valour.Database.AutomodLog
            {
                Id = Guid.NewGuid(),
                PlanetId = member.PlanetId,
                TriggerId = trigger.Id,
                MemberId = member.Id,
                MessageId = null,
                TimeTriggered = DateTime.UtcNow
            };
            await _db.AutomodLogs.AddAsync(log);
            await _db.SaveChangesAsync();

            actions = await FilterActionsByStrikesAsync(actions, member, trigger.Id);
            await RunActionsAsync(actions, member, null);
        }
    }
}
