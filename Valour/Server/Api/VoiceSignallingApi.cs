using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.API;

public class VoiceSignallingApi
{
    private const int MinimumRealtimeKitParticipants = 2;

    public static void AddRoutes(WebApplication app)
    {
        // Provider-neutral routes (canonical). The handlers act through IVoiceProvider,
        // so a single path serves whichever backend the instance runs.
        app.MapPost("api/voice/token/{channelId:long}", GetVoiceToken);
        app.MapPost("api/voice/channels/{channelId:long}/participants/{targetUserId:long}/mute", MuteParticipant);
        app.MapPost("api/voice/channels/{channelId:long}/participants/{targetUserId:long}/unmute", UnmuteParticipant);
        app.MapPost("api/voice/channels/{channelId:long}/participants/{targetUserId:long}/kick", KickParticipant);
        app.MapPost("api/voice/channels/{channelId:long}/leave", LeaveVoiceChannel);
        app.MapPost("api/voice/heartbeat", VoiceHeartbeat);

        // Legacy provider-named aliases, retained so already-loaded clients keep working.
        app.MapPost("api/voice/realtimekit/token/{channelId:long}", GetVoiceToken);
        app.MapPost("api/voice/realtimekit/channels/{channelId:long}/participants/{targetUserId:long}/mute", MuteParticipant);
        app.MapPost("api/voice/realtimekit/channels/{channelId:long}/participants/{targetUserId:long}/unmute", UnmuteParticipant);
        app.MapPost("api/voice/realtimekit/channels/{channelId:long}/participants/{targetUserId:long}/kick", KickParticipant);
        app.MapPost("api/voice/realtimekit/channels/{channelId:long}/leave", LeaveVoiceChannel);
        app.MapPost("api/voice/realtimekit/heartbeat", VoiceHeartbeat);
    }

    public static async Task<IResult> GetVoiceToken(
        ValourDb db,
        TokenService tokenService,
        PlanetMemberService memberService,
        CoreHubService coreHubService,
        IVoiceProvider voiceProvider,
        VoiceStateService voiceStateService,
        long channelId,
        string? sessionId)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null)
            return ValourResult.InvalidToken();

        var dbChannel = await db.Channels.FindAsync(channelId);
        if (dbChannel is null || !ISharedChannel.VoiceChannelTypes.Contains(dbChannel.ChannelType))
            return ValourResult.NotFound("Channel does not exist.");

        // Planet call channels are currently supported for RealtimeKit.
        if (!ISharedChannel.IsPlanetCallType(dbChannel.ChannelType) || dbChannel.PlanetId is null)
        {
            return ValourResult.BadRequest("RealtimeKit currently supports only planet voice or video channels.");
        }

        var channel = dbChannel.ToModel();

        var member = await memberService.GetByUserAsync(authToken.UserId, dbChannel.PlanetId.Value);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var hasViewPermission = await memberService.HasPermissionAsync(member, channel, VoiceChannelPermissions.View);
        if (!hasViewPermission)
            return ValourResult.LacksPermission(VoiceChannelPermissions.View);

        var hasJoinPermission = await memberService.HasPermissionAsync(member, channel, VoiceChannelPermissions.Join);
        if (!hasJoinPermission)
            return ValourResult.LacksPermission(VoiceChannelPermissions.Join);

        var dbUser = await db.Users.FindAsync(authToken.UserId);
        if (dbUser is null)
            return ValourResult.NotFound("User was not found.");

        var displayName = $"{dbUser.Name}#{dbUser.Tag}";

        // Track voice state in Redis + HostedPlanet
        var previousChannelId = await voiceStateService.UserJoinVoiceChannelAsync(
            authToken.UserId, channelId, dbChannel.PlanetId.Value, sessionId);

        // Server-side enforcement/backstop: ensure stale RTK media from a previous channel is torn down
        // before returning the new response.
        if (previousChannelId.HasValue && previousChannelId.Value != channelId)
        {
            await voiceProvider.KickUserFromTrackedChannelAsync(previousChannelId.Value, authToken.UserId);

            var oldChannelParticipants = await voiceStateService.GetChannelParticipantsAsync(previousChannelId.Value);
            if (oldChannelParticipants.Count < MinimumRealtimeKitParticipants)
            {
                await voiceProvider.CloseTrackedMeetingAsync(
                    previousChannelId.Value,
                    "user moved to another voice channel");
            }
        }

        // Always send session replace for this channel to eject any stale RTK peers
        coreHubService.NotifyVoiceSessionReplace(authToken.UserId, new VoiceSessionReplaceEvent
        {
            ChannelId = channel.Id,
            SessionId = sessionId ?? string.Empty
        });

        // If user was in a different channel, also eject them from the old channel
        if (previousChannelId.HasValue && previousChannelId.Value != channelId)
        {
            coreHubService.NotifyVoiceSessionReplace(authToken.UserId, new VoiceSessionReplaceEvent
            {
                ChannelId = previousChannelId.Value,
                SessionId = sessionId ?? string.Empty
            });
        }

        var participantCount = (await voiceStateService.GetChannelParticipantsAsync(channelId)).Count;
        if (participantCount < MinimumRealtimeKitParticipants)
        {
            await voiceProvider.CloseTrackedMeetingAsync(
                channelId,
                "voice channel is waiting for another participant");

            return ValourResult.Json(new RealtimeKitVoiceTokenResponse
            {
                Provider = voiceProvider.Kind.ToWire(),
                WaitingForPeer = true,
                ParticipantCount = participantCount
            });
        }

        TaskResult<RealtimeKitVoiceTokenResponse> tokenResult =
            await voiceProvider.CreateParticipantTokenAsync(channel, authToken.UserId, displayName, sessionId);

        if (!tokenResult.Success || tokenResult.Data is null)
        {
            await voiceStateService.UserLeaveVoiceChannelAsync(
                authToken.UserId, channelId, dbChannel.PlanetId.Value, sessionId);

            await voiceProvider.CloseTrackedMeetingAsync(
                channelId,
                "failed to create participant token");

            return ValourResult.BadRequest(tokenResult.Message);
        }

        tokenResult.Data.ParticipantCount = participantCount;

        return ValourResult.Json(tokenResult.Data);
    }

    public static async Task<IResult> MuteParticipant(
        ValourDb db,
        TokenService tokenService,
        PlanetMemberService memberService,
        NodeLifecycleService nodeLifecycleService,
        long channelId,
        long targetUserId)
    {
        var validation = await ValidateModerationRequestAsync(
            db,
            tokenService,
            memberService,
            channelId,
            targetUserId,
            VoiceChannelPermissions.MuteMembers);

        if (validation.Error is not null)
            return validation.Error;

        await nodeLifecycleService.RelayUserEventAsync(
            validation.TargetMember!.UserId,
            NodeLifecycleService.NodeEventType.VoiceModeration,
            new VoiceModerationEvent
            {
                ChannelId = validation.Channel!.Id,
                ModeratorUserId = validation.ActorMember!.UserId,
                TargetUserId = validation.TargetMember.UserId,
                Action = VoiceModerationActionType.Mute
            });

        return Results.Ok();
    }

    public static async Task<IResult> UnmuteParticipant(
        ValourDb db,
        TokenService tokenService,
        PlanetMemberService memberService,
        NodeLifecycleService nodeLifecycleService,
        long channelId,
        long targetUserId)
    {
        var validation = await ValidateModerationRequestAsync(
            db,
            tokenService,
            memberService,
            channelId,
            targetUserId,
            VoiceChannelPermissions.MuteMembers);

        if (validation.Error is not null)
            return validation.Error;

        await nodeLifecycleService.RelayUserEventAsync(
            validation.TargetMember!.UserId,
            NodeLifecycleService.NodeEventType.VoiceModeration,
            new VoiceModerationEvent
            {
                ChannelId = validation.Channel!.Id,
                ModeratorUserId = validation.ActorMember!.UserId,
                TargetUserId = validation.TargetMember.UserId,
                Action = VoiceModerationActionType.Unmute
            });

        return Results.Ok();
    }

    public static async Task<IResult> KickParticipant(
        ValourDb db,
        TokenService tokenService,
        PlanetMemberService memberService,
        NodeLifecycleService nodeLifecycleService,
        VoiceStateService voiceStateService,
        IVoiceProvider voiceProvider,
        long channelId,
        long targetUserId)
    {
        var validation = await ValidateModerationRequestAsync(
            db,
            tokenService,
            memberService,
            channelId,
            targetUserId,
            VoiceChannelPermissions.KickMembers);

        if (validation.Error is not null)
            return validation.Error;

        await nodeLifecycleService.RelayUserEventAsync(
            validation.TargetMember!.UserId,
            NodeLifecycleService.NodeEventType.VoiceModeration,
            new VoiceModerationEvent
            {
                ChannelId = validation.Channel!.Id,
                ModeratorUserId = validation.ActorMember!.UserId,
                TargetUserId = validation.TargetMember.UserId,
                Action = VoiceModerationActionType.Kick
            });

        // Remove kicked user from voice state
        var dbChannel = await db.Channels.FindAsync(channelId);
        if (dbChannel?.PlanetId is not null)
        {
            await voiceStateService.UserLeaveVoiceChannelAsync(
                targetUserId, channelId, dbChannel.PlanetId.Value);

            await voiceProvider.KickUserFromTrackedChannelAsync(channelId, targetUserId);

            var remainingParticipants = await voiceStateService.GetChannelParticipantsAsync(channelId);
            if (remainingParticipants.Count < MinimumRealtimeKitParticipants)
            {
                await voiceProvider.CloseTrackedMeetingAsync(
                    channelId,
                    "voice moderation left fewer than two participants");
            }
        }

        return Results.Ok();
    }

    public static async Task<IResult> LeaveVoiceChannel(
        ValourDb db,
        TokenService tokenService,
        PlanetMemberService memberService,
        IVoiceProvider voiceProvider,
        VoiceStateService voiceStateService,
        long channelId,
        string? sessionId)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null)
            return ValourResult.InvalidToken();

        var dbChannel = await db.Channels.FindAsync(channelId);
        if (dbChannel is null || !ISharedChannel.VoiceChannelTypes.Contains(dbChannel.ChannelType))
            return ValourResult.NotFound("Channel does not exist.");

        if (!ISharedChannel.IsPlanetCallType(dbChannel.ChannelType) || dbChannel.PlanetId is null)
            return ValourResult.BadRequest("Only planet voice/video channels are supported.");

        var member = await memberService.GetByUserAsync(authToken.UserId, dbChannel.PlanetId.Value);
        if (member is null)
            return ValourResult.NotPlanetMember();

        await voiceStateService.UserLeaveVoiceChannelAsync(
            authToken.UserId, channelId, dbChannel.PlanetId.Value, sessionId);

        // Best-effort exact teardown. With session-aware participant IDs this avoids
        // stale leave calls kicking a newer session from another window.
        await voiceProvider.KickUserSessionFromTrackedChannelAsync(channelId, authToken.UserId, sessionId);

        var remainingParticipants = await voiceStateService.GetChannelParticipantsAsync(channelId);
        if (remainingParticipants.Count < MinimumRealtimeKitParticipants)
        {
            await voiceProvider.CloseTrackedMeetingAsync(
                channelId,
                "voice channel dropped below the participant threshold");
        }

        return Results.Ok();
    }

    public static async Task<IResult> VoiceHeartbeat(
        TokenService tokenService,
        VoiceStateService voiceStateService)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null)
            return ValourResult.InvalidToken();

        await voiceStateService.RefreshVoiceHeartbeatAsync(authToken.UserId);

        return Results.Ok();
    }

    private static async Task<VoiceModerationValidationResult> ValidateModerationRequestAsync(
        ValourDb db,
        TokenService tokenService,
        PlanetMemberService memberService,
        long channelId,
        long targetUserId,
        VoiceChannelPermission requiredPermission)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
        if (authToken is null)
            return VoiceModerationValidationResult.FromError(ValourResult.InvalidToken());

        var dbChannel = await db.Channels.FindAsync(channelId);
        if (dbChannel is null || !ISharedChannel.VoiceChannelTypes.Contains(dbChannel.ChannelType))
            return VoiceModerationValidationResult.FromError(ValourResult.NotFound("Channel does not exist."));

        if (!ISharedChannel.IsPlanetCallType(dbChannel.ChannelType) || dbChannel.PlanetId is null)
        {
            return VoiceModerationValidationResult.FromError(
                ValourResult.BadRequest("Voice moderation currently supports only planet voice or video channels."));
        }

        var channel = dbChannel.ToModel();

        var actor = await memberService.GetByUserAsync(authToken.UserId, dbChannel.PlanetId.Value);
        if (actor is null)
            return VoiceModerationValidationResult.FromError(ValourResult.NotPlanetMember());

        if (!await memberService.HasPermissionAsync(actor, channel, VoiceChannelPermissions.View))
            return VoiceModerationValidationResult.FromError(ValourResult.LacksPermission(VoiceChannelPermissions.View));

        if (!await memberService.HasPermissionAsync(actor, channel, requiredPermission))
            return VoiceModerationValidationResult.FromError(ValourResult.LacksPermission(requiredPermission));

        var target = await memberService.GetByUserAsync(targetUserId, dbChannel.PlanetId.Value);
        if (target is null)
            return VoiceModerationValidationResult.FromError(ValourResult.NotFound("Target user is not a planet member."));

        if (target.UserId == actor.UserId)
            return VoiceModerationValidationResult.FromError(ValourResult.BadRequest("You cannot moderate yourself."));

        var actorAuthority = await memberService.GetAuthorityAsync(actor);
        var targetAuthority = await memberService.GetAuthorityAsync(target);
        if (targetAuthority >= actorAuthority)
        {
            return VoiceModerationValidationResult.FromError(
                ValourResult.Forbid("The target has equal or higher authority than you."));
        }

        return new VoiceModerationValidationResult
        {
            Channel = channel,
            ActorMember = actor,
            TargetMember = target
        };
    }

    private sealed class VoiceModerationValidationResult
    {
        public IResult? Error { get; set; }

        public Channel? Channel { get; set; }

        public PlanetMember? ActorMember { get; set; }

        public PlanetMember? TargetMember { get; set; }

        public static VoiceModerationValidationResult FromError(IResult error) => new()
        {
            Error = error
        };
    }
}
