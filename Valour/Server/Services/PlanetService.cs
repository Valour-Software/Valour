using Valour.Server.Database;
using Valour.Server.Database.Items.Channels.Planets;
using Valour.Server.Database.Items.Planets;

namespace Valour.Server.Services;

public class PlanetService
{
    private readonly ValourDB _db;
    
    public PlanetService(ValourDB db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns the planet with the given id
    /// </summary>
    public async ValueTask<Planet> GetAsync(long id) =>
        await _db.Planets.FindAsync(id);

    public async Task<PlanetChatChannel> GetPrimaryChannelAsync(long id)
    {
        var planet = await GetAsync(id);
        
    }
    
}