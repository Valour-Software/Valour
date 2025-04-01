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
    public ModelChange<TModel> Changes;
    public PositionChange? PositionChange;
    public readonly TModel Model;
    
    public TModel GetModel() => Model;
    
    public ModelUpdatedEvent(TModel model, ModelChange<TModel> changes, PositionChange? positionChange = null)
    {
        Model = model;
        Changes = changes;
        PositionChange = positionChange;
    }
    
    public void Dispose()
    {
        Changes?.Dispose();
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