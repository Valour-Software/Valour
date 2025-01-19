using Valour.Client.Components.Utility.DragList;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Components.PlanetsList;

public class PlanetListItem : DragListItem
{
    public Planet Planet { get; set; }

    public override uint Depth => 0u;
    public override uint Position => Planet.Name.FirstOrDefault();

    public PlanetListItem(Planet planet)
    {
        Planet = planet;
    }
}

public class ChannelListItem : DragListItem
{
    public Channel Channel { get; set; }
    
    public override uint Depth => Channel.Position.Depth;
    public override uint Position => Channel.RawPosition;
    
    public ChannelListItem(Channel channel)
    {
        Channel = channel;
    }
}