namespace Valour.Server.Services;

public class PlanetInviteService
{
    public async Task DeleteAsync(ValourDB db)
    {
        db.PlanetInvites.Remove(this);
    }
}