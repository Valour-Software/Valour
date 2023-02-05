namespace Valour.Server.Services;

public class PlanetChannelService
{
    private readonly ValourDB _db;

    public PlanetChannelService(
        ValourDB db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns the channel with the given id
    /// </summary>
    public async ValueTask<PlanetChannel> GetAsync(long id) =>
        (await _db.PlanetChannels.FindAsync(id)).ToModel();

    /// <summary>
    /// Returns all of the permission nodes for the given channel id
    /// </summary>
    public async Task<List<PermissionsNode>> GetPermNodesAsync(long channelId) =>
        (await _db.PermissionsNodes.Where(x => x.TargetId == channelId).Select(x => x.ToModel()).ToListAsync());
}