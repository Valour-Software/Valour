
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Server.Database;
using Valour.Server.Planets;

namespace Valour.Server.Roles;
public class PermissionsNode : Shared.Roles.PermissionsNode<PermissionsNode>
{
    [ForeignKey("Planet_Id")]
    public virtual ServerPlanet Planet { get; set; }

    [ForeignKey("Role_Id")]
    public virtual ServerPlanetRole Role { get; set; }

    /// <summary>
    /// This is a somewhat dirty way to fix the problem,
    /// but I need more time to figure out how to escape the generics hell
    /// i have created - spikey boy
    /// </summary>

    public async Task<IServerChannelListItem> GetTarget(ValourDB db)
    {
        switch (Target_Type)
        {
            case Shared.Items.ItemType.Channel: return await db.PlanetChatChannels.FindAsync(Target_Id);
            case Shared.Items.ItemType.Category: return await db.PlanetCategories.FindAsync(Target_Id);
        }

        return null;
    }
}
