using Microsoft.EntityFrameworkCore.Storage;
using Valour.Server.Database;
using Valour.Database.Context;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Server.Workers;

namespace Valour.Server.Services;

public class PlanetMessageService
{
    private readonly ValourDB _db;
    private readonly CoreHubService _coreHub;

    public PlanetMessageService(
        ValourDB db,
        CoreHubService coreHub)
    {
        _db = db;
        _coreHub = coreHub;
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
	public async Task DeleteAsync(PlanetChatChannel channel)
	{
		var dbchannel = channel.ToDatabase();
		dbchannel.IsDeleted = true;
		_db.PlanetChatChannels.Update(dbchannel);
		await _db.SaveChangesAsync();

		_coreHub.NotifyPlanetItemDelete(channel);
	}
}