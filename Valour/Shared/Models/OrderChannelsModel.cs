namespace Valour.Shared.Models;

public class OrderChannelsModel
{
    /// <summary>
    /// The order for the child channels and categories.
    /// </summary>
    public List<long> Order { get; set; }
    
    /// <summary>
    /// The category whose children are being ordered. If null, the root channels are being ordered.
    /// </summary>
    public long? CategoryId { get; set; }
    
    /// <summary>
    /// The planet for which these changes are being made.
    /// </summary>
    public long PlanetId { get; set; }
    
}