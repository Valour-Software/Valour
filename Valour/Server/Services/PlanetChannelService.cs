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
}