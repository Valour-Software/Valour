namespace Valour.Sdk.ModelLogic;


public struct PositionChange
{
    public uint OldPosition;
    public uint NewPosition;
}

public interface IModelEvent<TModel>
    where TModel : ClientModel {}

public interface IModelInsertionEvent<TModel> : IModelEvent<TModel>
    where TModel : ClientModel
{
    public TModel GetModel();
}

public readonly struct ModelsSetEvent<TModel> : IModelEvent<TModel>
    where TModel : ClientModel {}
public readonly struct ModelsClearedEvent<TModel> : IModelEvent<TModel>
    where TModel : ClientModel {}
public readonly struct ModelsOrderedEvent<TModel> : IModelEvent<TModel>
    where TModel : ClientModel {}
public readonly struct ModelAddedEvent<TModel> : IModelInsertionEvent<TModel> 
    where TModel : ClientModel
{
    public readonly TModel Model;
    public TModel GetModel() => Model;
    
    public ModelAddedEvent(TModel model)
    {
        Model = model;
    }
}
public class ModelUpdatedEvent<TModel> : IModelInsertionEvent<TModel>, IDisposable
    where TModel : ClientModel
{
    private bool _disposed = false;
    
    public HashSet<string> PropsChanged;
    public PositionChange? PositionChange;
    public readonly TModel Model;
    
    public TModel GetModel() => Model;
    
    public ModelUpdatedEvent(TModel model, HashSet<string> propsChanged, PositionChange? positionChange = null)
    {
        Model = model;
        PropsChanged = propsChanged;
        PositionChange = positionChange;
    }
    
    // Cleanup: Return PropsChanged to pool
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (PropsChanged != null)
            {
                ModelUpdateUtils.ReturnPropsChanged(PropsChanged);
                PropsChanged = null;
            }
        }
        
        _disposed = true;
    }

    ~ModelUpdatedEvent()
    {
        Dispose(false);
    }
}

public readonly struct ModelRemovedEvent<TModel> : IModelEvent<TModel>
    where TModel : ClientModel
{
    public readonly TModel Model;
    
    public ModelRemovedEvent(TModel model)
    {
        Model = model;
    }
}