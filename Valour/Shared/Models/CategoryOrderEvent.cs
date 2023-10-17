namespace Valour.Shared.Models;


public struct ChannelOrderData
{
    public long Id { get; set; }
    public ChannelTypeEnum ChannelType { get; set; }
    
    public ChannelOrderData(long id, ChannelTypeEnum type)
    {
        Id = id;
        ChannelType = type;
    }
}

public class CategoryOrderEvent
{
    public long PlanetId { get; set; }
    public long? CategoryId { get; set; }
    public List<ChannelOrderData> Order { get; set; }
}