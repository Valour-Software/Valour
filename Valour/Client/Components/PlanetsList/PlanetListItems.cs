using Valour.Client.Components.Utility.DragList;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Components.PlanetsList;

public class PlanetListItem : DragListItem
{
    public Planet Planet { get; set; }

    public override int Depth => 0;
    public override int Position => Planet.Name.FirstOrDefault();

    public PlanetListItem(Planet planet)
    {
        Planet = planet;
    }
}

public class ChannelListItem : DragListItem
{
    public Channel Channel { get; set; }
    
    public override int Depth => Channel.Depth;
    public override int Position => Channel.Position;
    
    public ChannelListItem(Channel channel)
    {
        Channel = channel;
    }
}