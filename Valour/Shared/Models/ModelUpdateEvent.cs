namespace Valour.Shared.Models;

/// <summary>
/// This is run on the model itself, so we don't need to include it
/// </summary>
public class ModelUpdateEvent
{
    /// <summary>
    /// The fields that changed on the item
    /// </summary>
    public HashSet<string> PropsChanged { get; set; } = new HashSet<string>();

    /// <summary>
    /// Additional data for the event
    /// </summary>
    public int? Flags { get; set; }
}

public class ModelUpdateEvent<T> where T : ISharedItem
{
    /// <summary>
    /// The new or updated item
    /// </summary>
    public T Model { get; set; }

    /// <summary>
    /// The fields that changed on the item
    /// </summary>
    public HashSet<string> FieldsChanged { get; set; } = new HashSet<string>();
    
    /// <summary>
    /// True if the item is new to the client
    /// </summary>
    public bool NewToClient { get; set; }
    
    /// <summary>
    /// Additional data for the event
    /// </summary>
    public int? Flags { get; set; }
}