using Valour.Shared.Utilities;

namespace Valour.Sdk.ModelLogic;

/// <summary>
/// The ModelObserver class allows global events to be hooked for entire item types
/// </summary>
public static class ModelObserver<T> where T : ClientModel
{
    /// <summary>
    /// Run when any of this item type is updated
    /// </summary>
    public static HybridEvent<ModelUpdateEvent<T>> AnyUpdated;

    /// <summary>
    /// Run when any of this item type is deleted
    /// </summary>
    public static HybridEvent<T> AnyDeleted;

    public static async Task InvokeAnyUpdated(ModelUpdateEvent<T> eventData)
    {
        if (AnyUpdated != null)
            await AnyUpdated.Invoke(eventData);
    }

    public static async Task InvokeAnyDeleted(T deleted)
    {
        if (AnyDeleted != null)
            await AnyDeleted.Invoke(deleted);
    }
}

