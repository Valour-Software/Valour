namespace Valour.Shared.Models;


public struct ChannelOrderData
{
    public long Id { get; set; }
    public ChannelType Type { get; set; }
    
    public ChannelOrderData(long id, ChannelType type)
    {
        Id = id;
        Type = type;
    }
}

public class CategoryOrderEvent
{
    public long PlanetId { get; set; }
    public long CategoryId { get; set; }
    public List<ChannelOrderData> Order { get; set; }
}