using Valour.Client.Components.Utility.DragList;
using Valour.Sdk.Models;
using Valour.Shared.Models;

namespace Valour.Client.Components.PlanetsList;

public class PlanetListItem : DragListItem
{
    public Planet Planet { get; set; }

    private DragListItem _parent;
    private List<DragListItem> _children;
    
    public PlanetListItem(Planet planet)
    {
        Planet = planet;
    }
}

public class ChannelListItem : DragListItem
{
    public Channel Channel { get; set; }
    
    private DragListItem _parent;
    private List<DragListItem> _children;
    
    public ChannelListItem(Channel channel)
    {
        Channel = channel;
    }

    public override async Task<DragListItem> GetDragParent()
    {
        if (_parent is null)
        {
            if (Channel.ParentId is null)
            {
                var planet = await Channel.GetPlanetAsync();
                if (planet is null)
                    return null;
                
                _parent = new PlanetListItem(planet);
            }
            else
            {
                var category = await Channel.GetParentAsync();
                if (category is null)
                    return null;
                
                _parent = new ChannelListItem(category);
            }
        }
            
        return _parent;
    }
    
    public override List<DragListItem> GetDragChildren()
    {
        // only categories have children
        if (Channel.ChannelType != ChannelTypeEnum.PlanetCategory)
            return null;

        if (_children is null)
        {
            _children = new List<DragListItem>();
            var channels = await Channel.GetChildrenAsync();
        }
    }
}