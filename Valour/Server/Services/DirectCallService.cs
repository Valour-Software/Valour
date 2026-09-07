using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using StackExchange.Redis;
using Valour.Server.Redis;
using DbCall = Valour.Database.DirectCall;
using DbCallMember = Valour.Database.DirectCallMember;
using CallModel = Valour.Shared.Models.DirectCall;
using CallMemberModel = Valour.Shared.Models.DirectCallMember;

namespace Valour.Server.Services;

public class DirectCallService
{
    private static readonly TimeSpan ParticipantTokenLifetime = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(45);

    private readonly ValourDb _db;
    private readonly ChannelService _channelService;
    private readonly UserBlockService _userBlockService;
    private readonly CoreHubService _coreHubService;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly IVoiceProvider _voiceProvider;
    private readonly IConnectionMultiplexer _redis;

    public DirectCallService(
        ValourDb db,
        ChannelService channelService,
        UserBlockService userBlockService,
        CoreHubService coreHubService,
        NodeLifecycleService nodeLifecycleService,
        IVoiceProvider voiceProvider,
        IConnectionMultiplexer redis)
    {
        _db = db;
        _channelService = channelService;
        _userBlockService = userBlockService;
        _coreHubService = coreHubService;
        _nodeLifecycleService = nodeLifecycleService;
        _voiceProvider = voiceProvider;
        _redis = redis;
    }

    public async Task<TaskResult<CallModel>> StartAsync(long callerUserId, StartDirectCallRequest request)
    {
        if (!Enum.IsDefined(request.Kind))
            return TaskResult<CallModel>.FromFailure("Invalid call type.");

        var channel = await _db.Channels
            .AsNoTracking()
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == request.ChannelId && !x.IsDeleted);

        if (channel is null || channel.PlanetId is not null ||
            channel.ChannelType is not (ChannelTypeEnum.DirectChat or ChannelTypeEnum.GroupChat))
            return TaskResult<CallModel>.FromFailure("Direct message channel not found.");
        if (!channel.Members.Any(x => x.UserId == callerUserId))
            return TaskResult<CallModel>.FromFailure("You are not a member of this conversation.");

        var targetUserIds = channel.Members.Select(x => x.UserId).Where(x => x != callerUserId).Distinct().ToList();
        if (targetUserIds.Count == 0)
            return TaskResult<CallModel>.FromFailure("A call needs at least one other person.");

        var privacyResult = await ValidateCallRecipientsAsync(callerUserId, targetUserIds);
        if (!privacyResult.Success)
            return TaskResult<CallModel>.FromFailure(privacyResult);

        var allUserIds = targetUserIds.Append(callerUserId).ToList();
        if (await HasConflictingCallAsync(allUserIds))
            return TaskResult<CallModel>.FromFailure("Someone in this conversation is already in another call.");

        var now = DateTime.UtcNow;
        var call = new DbCall
        {
            Id = IdManager.Generate(),
            ChannelId = channel.Id,
            CallerUserId = callerUserId,
            Kind = request.Kind,
            State = DirectCallState.Ringing,
            EndReason = DirectCallEndReason.None,
            CreatedAt = now,
            ExpiresAt = now.Add(RingTimeout),
            Members = allUserIds.Select(userId => new DbCallMember
            {
                Id = IdManager.Generate(),
                UserId = userId,
                IsCaller = userId == callerUserId,
                State = userId == callerUserId
                    ? DirectCallMemberState.Joined
                    : DirectCallMemberState.Invited,
                RespondedAt = userId == callerUserId ? now : null
            }).ToList()
        };

        await _db.DirectCalls.AddAsync(call);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return TaskResult<CallModel>.FromFailure("Someone in this conversation is already in another call.");
        }

        var model = ToModel(call);
        await RelayAsync(model);
        return TaskResult<CallModel>.FromData(model);
    }

    public async Task<List<CallModel>> GetCurrentAsync(long userId)
    {
        await ExpireRingingCallsAsync();
        var calls = await _db.DirectCalls.AsNoTracking()
            .Include(x => x.Members)
            .Where(x => x.State != DirectCallState.Ended && x.Members.Any(m => m.UserId == userId))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
        return calls.Select(ToModel).ToList();
    }

    public async Task<CallModel?> GetAsync(long callId, long userId)
    {
        var call = await FindCallAsync(callId);
        return call?.Members.Any(x => x.UserId == userId) == true ? ToModel(call) : null;
    }

    public async Task<TaskResult<CallModel>> AcceptAsync(long callId, long userId)
    {
        var call = await FindCallAsync(callId);
        if (call is null || call.State == DirectCallState.Ended)
            return TaskResult<CallModel>.FromFailure("Call not found.");
        if (call.State == DirectCallState.Ringing && call.ExpiresAt <= DateTime.UtcNow)
        {
            await EndCoreAsync(call, DirectCallEndReason.Missed);
            return TaskResult<CallModel>.FromFailure("This call was missed.");
        }

        var member = call.Members.FirstOrDefault(x => x.UserId == userId);
        if (member is null || member.State != DirectCallMemberState.Invited)
            return TaskResult<CallModel>.FromFailure("You do not have a pending invitation to this call.");
        if (await HasConflictingCallAsync([userId], call.Id))
            return TaskResult<CallModel>.FromFailure("Leave your other call before joining this one.");

        member.State = DirectCallMemberState.Joined;
        member.RespondedAt = DateTime.UtcNow;
        if (call.State == DirectCallState.Ringing)
        {
            call.State = DirectCallState.Active;
            call.AcceptedAt = DateTime.UtcNow;
        }

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return TaskResult<CallModel>.FromFailure("Leave your other call before joining this one.");
        }
        var model = ToModel(call);
        await RelayAsync(model);
        return TaskResult<CallModel>.FromData(model);
    }

    public async Task<TaskResult<CallModel>> DeclineAsync(long callId, long userId)
    {
        var call = await FindCallAsync(callId);
        if (call is null || call.State == DirectCallState.Ended)
            return TaskResult<CallModel>.FromFailure("Call not found.");

        var member = call.Members.FirstOrDefault(x => x.UserId == userId);
        if (member is null || member.IsCaller || member.State != DirectCallMemberState.Invited)
            return TaskResult<CallModel>.FromFailure("You do not have a pending invitation to this call.");

        member.State = DirectCallMemberState.Declined;
        member.RespondedAt = DateTime.UtcNow;
        if (call.Members.All(x => x.IsCaller || x.State != DirectCallMemberState.Invited) &&
            call.Members.Count(x => x.State == DirectCallMemberState.Joined) < 2)
        {
            await EndCoreAsync(call, DirectCallEndReason.Declined);
        }
        else
        {
            await _db.SaveChangesAsync();
            await RelayAsync(ToModel(call));
        }

        return TaskResult<CallModel>.FromData(ToModel(call));
    }

    public async Task<TaskResult<CallModel>> LeaveAsync(long callId, long userId, string? sessionId)
    {
        var call = await FindCallAsync(callId);
        if (call is null || call.State == DirectCallState.Ended)
            return TaskResult<CallModel>.FromFailure("Call not found.");

        var member = call.Members.FirstOrDefault(x => x.UserId == userId);
        if (member is null || member.State is not (DirectCallMemberState.Joined or DirectCallMemberState.Invited))
            return TaskResult<CallModel>.FromFailure("You are not in this call.");

        member.State = DirectCallMemberState.Left;
        member.RespondedAt = DateTime.UtcNow;
        await _voiceProvider.KickUserSessionFromTrackedChannelAsync(call.Id, userId, sessionId);

        if (member.IsCaller && call.State == DirectCallState.Ringing)
            await EndCoreAsync(call, DirectCallEndReason.Cancelled);
        else if (call.Members.Count(x => x.State == DirectCallMemberState.Joined) < 2 &&
                 !call.Members.Any(x => x.State == DirectCallMemberState.Invited))
            await EndCoreAsync(call, DirectCallEndReason.Completed);
        else
        {
            await _db.SaveChangesAsync();
            await RelayAsync(ToModel(call));
        }

        return TaskResult<CallModel>.FromData(ToModel(call));
    }

    public async Task<TaskResult<CallModel>> EndAsync(long callId, long userId)
    {
        var call = await FindCallAsync(callId);
        if (call is null || call.State == DirectCallState.Ended)
            return TaskResult<CallModel>.FromFailure("Call not found.");
        if (call.CallerUserId != userId)
            return TaskResult<CallModel>.FromFailure("Only the caller can end the call for everyone.");

        await EndCoreAsync(call, call.State == DirectCallState.Ringing
            ? DirectCallEndReason.Cancelled
            : DirectCallEndReason.Completed);
        return TaskResult<CallModel>.FromData(ToModel(call));
    }

    public async Task<TaskResult<CallModel>> AddParticipantsAsync(
        long callId,
        long actingUserId,
        AddDirectCallParticipantsRequest request)
    {
        var call = await FindCallAsync(callId);
        if (call is null || call.State == DirectCallState.Ended)
            return TaskResult<CallModel>.FromFailure("Call not found.");
        if (!call.Members.Any(x => x.UserId == actingUserId && x.State == DirectCallMemberState.Joined))
            return TaskResult<CallModel>.FromFailure("Join the call before inviting other people.");

        var newUserIds = request.UserIds.Distinct()
            .Where(x => call.Members.All(m => m.UserId != x))
            .ToList();
        if (newUserIds.Count == 0)
            return TaskResult<CallModel>.FromData(ToModel(call));

        var privacyResult = await ValidateCallRecipientsAsync(actingUserId, newUserIds);
        if (!privacyResult.Success)
            return TaskResult<CallModel>.FromFailure(privacyResult);
        if (await HasConflictingCallAsync(newUserIds))
            return TaskResult<CallModel>.FromFailure("One or more people are already in another call.");

        // Reuse an ambient transaction when the operation is composed by another
        // service or an integration test. Production API calls normally own this
        // transaction, preserving the channel/call membership atomicity.
        await using var transaction = _db.Database.CurrentTransaction is null
            ? await _db.Database.BeginTransactionAsync()
            : null;
        var channelResult = await _channelService.ConvertDirectDmToGroupAsync(
            call.ChannelId, actingUserId, newUserIds);
        if (!channelResult.Success)
            return TaskResult<CallModel>.FromFailure(channelResult);

        call.ChannelId = channelResult.Data.Id;

        foreach (var userId in newUserIds)
        {
            var member = new DbCallMember
            {
                Id = IdManager.Generate(),
                CallId = call.Id,
                UserId = userId,
                State = DirectCallMemberState.Invited
            };
            call.Members.Add(member);
            _db.DirectCallMembers.Add(member);
        }

        call.ExpiresAt = DateTime.UtcNow.Add(RingTimeout);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync();
            return TaskResult<CallModel>.FromFailure("One or more people are already in another call.");
        }
        if (transaction is not null)
            await transaction.CommitAsync();
        await _coreHubService.RelayDirectChannelUpdate(
            channelResult.Data,
            _nodeLifecycleService,
            channelResult.Data.Members.Select(x => x.UserId));
        var model = ToModel(call);
        await RelayAsync(model);
        return TaskResult<CallModel>.FromData(model);
    }

    public async Task<TaskResult<RealtimeKitVoiceTokenResponse>> CreateTokenAsync(
        long callId,
        long userId,
        string? sessionId)
    {
        var call = await FindCallAsync(callId);
        if (call is null || call.State != DirectCallState.Active)
            return TaskResult<RealtimeKitVoiceTokenResponse>.FromFailure("Call is not active.");
        if (!call.Members.Any(x => x.UserId == userId && x.State == DirectCallMemberState.Joined))
            return TaskResult<RealtimeKitVoiceTokenResponse>.FromFailure("Accept the call before joining.");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
            return TaskResult<RealtimeKitVoiceTokenResponse>.FromFailure("User not found.");

        var channel = new Valour.Server.Models.Channel
        {
            Id = call.Id,
            PlanetId = null,
            Name = $"Direct call {call.Id}",
            ChannelType = call.Kind == DirectCallKind.Video
                ? ChannelTypeEnum.PlanetVideo
                : call.Members.Count > 2 ? ChannelTypeEnum.GroupVoice : ChannelTypeEnum.DirectVoice
        };

        return await _voiceProvider.CreateParticipantTokenAsync(
            channel,
            userId,
            user.Name,
            sessionId,
            ParticipantTokenLifetime);
    }

    public async Task<int> ExpireRingingCallsAsync()
    {
        var calls = await _db.DirectCalls
            .Include(x => x.Members)
            .Where(x => x.ExpiresAt <= DateTime.UtcNow &&
                        (x.State == DirectCallState.Ringing ||
                         (x.State == DirectCallState.Active &&
                          x.Members.Any(m => m.State == DirectCallMemberState.Invited))))
            .ToListAsync();
        foreach (var call in calls)
        {
            if (call.State == DirectCallState.Ringing)
            {
                await EndCoreAsync(call, DirectCallEndReason.Missed);
                continue;
            }

            foreach (var invited in call.Members.Where(x => x.State == DirectCallMemberState.Invited))
            {
                invited.State = DirectCallMemberState.Left;
                invited.RespondedAt = DateTime.UtcNow;
            }

            if (call.Members.Count(x => x.State == DirectCallMemberState.Joined) < 2)
                await EndCoreAsync(call, DirectCallEndReason.Completed);
            else
            {
                await _db.SaveChangesAsync();
                await RelayAsync(ToModel(call));
            }
        }
        return calls.Count;
    }

    private async Task<DbCall?> FindCallAsync(long callId) =>
        await _db.DirectCalls.Include(x => x.Members).FirstOrDefaultAsync(x => x.Id == callId);

    private async Task<bool> HasConflictingCallAsync(IEnumerable<long> userIds, long? excludedCallId = null)
    {
        var ids = userIds.Distinct().ToList();
        if (await _db.DirectCallMembers.AnyAsync(x =>
            ids.Contains(x.UserId) &&
            x.Call.State != DirectCallState.Ended &&
            (!excludedCallId.HasValue || x.CallId != excludedCallId.Value) &&
            (x.State == DirectCallMemberState.Invited || x.State == DirectCallMemberState.Joined)))
            return true;

        var redisDb = _redis.GetDatabase(RedisDbTypes.Cluster);
        foreach (var userId in ids)
        {
            if (await redisDb.KeyExistsAsync($"voice:user:{userId}"))
                return true;
        }

        return false;
    }

    private async Task<TaskResult> ValidateCallRecipientsAsync(long callerUserId, IEnumerable<long> targetUserIds)
    {
        foreach (var targetUserId in targetUserIds.Distinct())
        {
            if (await _userBlockService.IsBlockedEitherWayAsync(callerUserId, targetUserId))
                return TaskResult.FromFailure("One or more people cannot be called.");

            var preferences = await _db.UserPreferences.FindAsync(targetUserId);
            if ((preferences?.CallPolicy ?? DmPolicy.FriendsOnly) != DmPolicy.FriendsOnly)
                continue;

            var mutualFriends = await _db.UserFriends.AnyAsync(
                x => x.UserId == callerUserId && x.FriendId == targetUserId) &&
                await _db.UserFriends.AnyAsync(
                    x => x.UserId == targetUserId && x.FriendId == callerUserId);
            if (!mutualFriends)
                return TaskResult.FromFailure("One or more people only accept calls from friends.");
        }

        return TaskResult.SuccessResult;
    }

    private async Task EndCoreAsync(DbCall call, DirectCallEndReason reason)
    {
        if (call.State == DirectCallState.Ended)
            return;

        call.State = DirectCallState.Ended;
        call.EndReason = reason;
        call.EndedAt = DateTime.UtcNow;
        foreach (var member in call.Members.Where(x => x.State is DirectCallMemberState.Joined or DirectCallMemberState.Invited))
        {
            member.State = DirectCallMemberState.Left;
            member.RespondedAt = call.EndedAt;
        }

        await _db.SaveChangesAsync();
        await _voiceProvider.CloseTrackedMeetingAsync(call.Id, reason.ToString());
        await RelayAsync(ToModel(call));
    }

    private async Task RelayAsync(CallModel call)
    {
        await _coreHubService.RelayDirectCallUpdate(
            call,
            _nodeLifecycleService,
            call.Members.Select(x => x.UserId).Distinct().ToList());
    }

    public static CallModel ToModel(DbCall call) => new()
    {
        Id = call.Id,
        ChannelId = call.ChannelId,
        CallerUserId = call.CallerUserId,
        Kind = call.Kind,
        State = call.State,
        EndReason = call.EndReason,
        CreatedAt = call.CreatedAt,
        ExpiresAt = call.ExpiresAt,
        AcceptedAt = call.AcceptedAt,
        EndedAt = call.EndedAt,
        Members = call.Members.Select(x => new CallMemberModel
        {
            UserId = x.UserId,
            IsCaller = x.IsCaller,
            State = x.State,
            RespondedAt = x.RespondedAt
        }).ToList()
    };
}
