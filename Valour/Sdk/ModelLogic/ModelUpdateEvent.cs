namespace Valour.Sdk.ModelLogic;

public struct PositionChange
{
    public uint OldPosition { get; set; }
    public uint NewPosition { get; set; }
}

public class ModelUpdateEvent<TModel> : IDisposable
    where TModel : ClientModel
{
    private bool _disposed = false;
    
    /// <summary>
    /// The new or updated item
    /// </summary>
    public TModel Model { get; set; }

    /// <summary>
    /// The fields that changed on the item
    /// </summary>
    public HashSet<string> PropsChanged { get; set; }
    
    /// <summary>
    /// If not null, the position change of the item
    /// </summary>
    public PositionChange PositionChange { get; set; }
    
    /// <summary>
    /// True if the item is new to the client
    /// </summary>
    public bool NewToClient { get; set; }
    
    /// <summary>
    /// Additional data for the event
    /// </summary>
    public int? Flags { get; set; }
    
    // Cleanup: Return PropsChanged to pool
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (PropsChanged != null)
            {
                ModelUpdater.ReturnPropsChanged(PropsChanged);
                PropsChanged = null;
            }
        }
        
        _disposed = true;
    }

    ~ModelUpdateEvent()
    {
        Dispose(false);
    }
}