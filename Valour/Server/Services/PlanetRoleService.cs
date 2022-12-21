
using Valour.Server.Database;
using Valour.Server.Database.Items.Planets.Members;

namespace Valour.Server.Services;

public class PlanetRoleService
{
    private readonly ValourDB _db;
    
    public PlanetRoleService(ValourDB db)
    {
        _db = db;
    }
    
    public ValueTask<PlanetRole> GetAsync(long id) =>
        _db.PlanetRoles.FindAsync(id);
}