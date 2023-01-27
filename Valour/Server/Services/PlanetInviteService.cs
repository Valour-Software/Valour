namespace Valour.Server.Services;

public class PlanetInviteService
{
    private readonly ValourDB _db;

    public PlanetInviteService(ValourDB db)
    {
        _db = db;
    }

    public async Task<PlanetInvite> GetAsync(long id) => 
        (await _db.PlanetInvites.FindAsync(id)).ToModel();
    
    public async Task<PlanetInvite> GetAsync(string code, long planetId) => 
        (await _db.PlanetInvites.FirstOrDefaultAsync(x => x.Code == code 
                                                                 && x.PlanetId == planetId))
        .ToModel();
    
    
    public async Task DeleteAsync(PlanetInvite invite)
    {
        _db.PlanetInvites.Remove(invite.ToDatabase());
        await _db.SaveChangesAsync();
    }
}