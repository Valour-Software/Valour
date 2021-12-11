using System.Text.Json.Serialization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Channels;

public interface IPlanetChannel
{
    [JsonInclude]
    [JsonPropertyName("Id")]
    public ulong Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("Planet_Id")]
    public ulong Planet_Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("Parent_Id")]
    public ulong? Parent_Id { get; set; }

    [JsonInclude]
    [JsonPropertyName("Position")]
    public ushort Position { get; set; }

    [JsonInclude]
    [JsonPropertyName("ItemType")]
    public ItemType ItemType { get; }

    [JsonIgnore]
    public Planet Planet { get; set; }

    public static async Task<IPlanetChannel> FindAsync(ItemType type, ulong id, ValourDB db)
    {
        switch (type)
        {
            case ItemType.Channel:
                return await ChatChannel.FindAsync(id, db);
            case ItemType.Category:
                return await Category.FindAsync(id, db);
            default:
                throw new ArgumentOutOfRangeException(nameof(ItemType));
        }
    }

    public Task<Planet> GetPlanetAsync(ValourDB db);

    public void NotifyClientsChange();

    public Task<bool> HasPermission(PlanetMember member, Permission permission, ValourDB db);
}
