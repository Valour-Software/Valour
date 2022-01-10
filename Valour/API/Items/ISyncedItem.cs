namespace Valour.Api.Items;

public interface ISyncedItem<T> {

    /// <summary>
    /// Ran when this item is updated
    /// </summary>
    public event Func<int, Task> OnUpdated;

    /// <summary>
    /// Ran when this item is deleted
    /// </summary>
    public event Func<Task> OnDeleted;

    public ulong Id { get; set;}

    public Task OnUpdate(int flags);

    public Task InvokeUpdated(int flags);

    public Task InvokeDeleted();

    public Task InvokeAnyUpdated(T updated, int flags);

    public Task InvokeAnyDeleted(T deleted);
}