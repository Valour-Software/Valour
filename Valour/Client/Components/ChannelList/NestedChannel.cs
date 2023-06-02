using Valour.Api.Models;

namespace Valour.Client.Components.ChannelList;

public class NestedChannel
{
    public long ChannelId { get; set; }
    public bool IsCategory { get; set; }
    public string Name { get; set; }
    public List<NestedChannel> Children { get; set; }

    public NestedChannel()
    {
        
    }
    
    public NestedChannel(PlanetChannel channel)
    {
        ChannelId = channel.Id;
        Name = channel.Name;
        IsCategory = channel is PlanetCategory;
    }
    
    public async Task LoadChildren(List<PlanetChannel> allChannels)
    {
        Console.WriteLine("Loading children");
        
        // Categories load their children
        if (IsCategory)
        {
            Children = allChannels
                .Where(x => x.ParentId == ChannelId)
                .OrderByDescending(x => x.Position)
                .Select(x => new NestedChannel(x))
                .ToList();

            foreach (var child in Children)
            {
                await child.LoadChildren(allChannels);
            }
        }
        else
        {
            Children = new List<NestedChannel>();
        }

        // We load ourself after our children because it's easier to know if we are unread
        await LoadSelf();
    }
    
    public async Task LoadSelf()
    {
        
    }
    
}