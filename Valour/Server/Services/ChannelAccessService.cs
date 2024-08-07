﻿using Valour.Database;
using Valour.Shared;
using Channel = Valour.Server.Models.Channel;
using PlanetMember = Valour.Server.Models.PlanetMember;

namespace Valour.Server.Services;

/// <summary>
/// Provides methods for handling channel access for planets
/// </summary>
public class ChannelAccessService
{
    private readonly ValourDB _db;
    private readonly ILogger<ChannelAccessService> _logger;
    
    public ChannelAccessService(ValourDB db, ILogger<ChannelAccessService> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    public Task<TaskResult<UpdateAccessResult>> UpdateChannelAccess(PlanetMember member, Channel channel)
    {
        return UpdateChannelAccess(member.Id, channel.Id);
    }

    /// <summary>
    /// Calls the database procedure for calculating and applying channel access
    /// </summary>
    public async Task<TaskResult<UpdateAccessResult>> UpdateChannelAccess(long memberId, long channelId)
    {
        try
        {
            var result = await _db.Set<UpdateAccessResult>()
                .FromSqlInterpolated($@"
                SELECT * FROM apply_member_access(
                    {memberId}, 
                    {channelId}
                )")
                .AsNoTracking()
                .SingleOrDefaultAsync();

            return TaskResult<UpdateAccessResult>.FromData(result);
        } 
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to update channel access for member {memberId} and channel {channelId}", memberId, channelId);
            return TaskResult<UpdateAccessResult>.FromError("Failed to update channel access");
        }
    }

    /// <summary>
    /// Updates all channel access in planet for the given member
    /// </summary>
    public async Task<UpdateAccessRowCountResult> UpdateAllChannelAccessMember(long memberId)
    {
        var result = await _db.Set<UpdateAccessRowCountResult>()
            .FromSqlInterpolated($@"
                SELECT apply_member_access_planet(
                    {memberId}
                ) as value")
            .AsNoTracking()
            .SingleOrDefaultAsync();

        return result;
    }

    /// <summary>
    /// Updates all channel access in planet for all members in the given role
    /// </summary>
    public async Task<UpdateAccessRowCountResult> UpdateAllChannelAccessForMembersInRole(long roleId)
    {
        var result = await _db.Set<UpdateAccessRowCountResult>()
            .FromSqlInterpolated($@"
                SELECT apply_member_access_for_all_in_role(
                    {roleId}
                ) as value")
            .AsNoTracking()
            .SingleOrDefaultAsync();
        
        return result;
    }

    /// <summary>
    /// Updates all channel access for a given channel
    /// </summary>
    /// <param name="channelId"></param>
    /// <returns></returns>
    public async Task<UpdateAccessRowCountResult> UpdateAllChannelAccessForChannel(long channelId)
    {
        var result = await _db.Set<UpdateAccessRowCountResult>()
            .FromSqlInterpolated($@"
                SELECT apply_member_access_channel_all(
                    {channelId}
                ) as value")
            .AsNoTracking()
            .SingleOrDefaultAsync();
        
        return result;
    }

    /// <summary>
    /// Removes all the channel access for a given member
    /// </summary>
    public async Task<UpdateAccessRowCountResult> ClearMemberAccessAsync(long memberId)
    {
        try
        {
            var count = await _db.MemberChannelAccess.Where(x => x.MemberId == memberId).ExecuteDeleteAsync();
            return new UpdateAccessRowCountResult()
            {
                RowsUpdated = count
            };
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to remove all channel access for member {memberId}", memberId);
            return new UpdateAccessRowCountResult()
            {
                RowsUpdated = 0
            };
        }
    }
    
}