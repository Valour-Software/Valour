using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database;
using Valour.Database.Context;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Server.Workers;
using IdGen;

namespace Valour.Server.Services;

public class PlanetMessageService
{
    private readonly ValourDB _db;
    private readonly CoreHubService _coreHub;
    private readonly PlanetMemberService _memberService;
    private readonly ILogger<PlanetChatChannelService> _logger;

    public PlanetMessageService(
        ValourDB db,
        CoreHubService coreHub,
        PlanetMemberService memberService,
        ILogger<PlanetChatChannelService> logger)
    {
        _db = db;
        _coreHub = coreHub;
        _memberService = memberService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the message with the given id
    /// </summary>
    public async ValueTask<PlanetMessage> GetAsync(long id)
    {
        var message = (await _db.PlanetMembers.FindAsync(id)).ToModel();
        if (message is null)
            message = PlanetMessageWorker.GetStagedMessage(id);
        return message;
    }

	/// <summary>
	/// Soft deletes the given channel
	/// </summary>
	public async Task<IResult> DeleteAsync(PlanetChatChannel channel, PlanetMember member, long message_id)
	{
        var message = await _db.PlanetMessages.FindAsync(message_id);

        var inDb = true;

        if (message is null)
        {
            inDb = false;

            // Try to find in staged
            message = PlanetMessageWorker.GetStagedMessage(message_id);
            if (message is null)
                return ValourResult.NotFound<PlanetMessage>();
        }

        if (message.ChannelId != channel.Id)
            return ValourResult.NotFound<PlanetMessage>();

        if (member.Id != message.AuthorMemberId)
        {
            if (!await _memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.ManageMessages))
                return ValourResult.LacksPermission(ChatChannelPermissions.ManageMessages);
        }

        // Remove from staging
        PlanetMessageWorker.RemoveFromQueue(message);

        // If in db, remove from db
        if (inDb)
        {
            try
            {
                _db.PlanetMessages.Remove(message);
                await _db.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                _logger.LogError(e.Message);
                return Results.Problem(e.Message);
            }
        }

        _coreHub.NotifyMessageDeletion(message);

        return Results.NoContent();
    }
}