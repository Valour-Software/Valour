namespace Valour.Shared.Models;

public class InsertChannelChildModel
{
    /// <summary>
    /// The planet this operation is occuring within.
    /// </summary>
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The item being inserted.
    /// </summary>
    public long InsertId { get; set; }
    
    /// <summary>
    /// The parent this item is being inserted into, if null this means the channel is top-level.
    /// </summary>
    public long? ParentId { get; set; }
    
    /// <summary>
    /// The position to insert the channel at. If null, it will be inserted at the end.
    /// </summary>
    public int? Position { get; set; }
}