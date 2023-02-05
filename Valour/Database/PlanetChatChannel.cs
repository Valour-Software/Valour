using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_chat_channels")]
public class PlanetChatChannel : PlanetChannel, ISharedPlanetChatChannel
{
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    public override PermChannelType PermType => PermChannelType.PlanetChatChannel;
}

