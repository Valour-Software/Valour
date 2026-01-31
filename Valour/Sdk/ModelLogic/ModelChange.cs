#nullable enable

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Valour.Sdk.ModelLogic;

/// <summary>
/// Represents a change for a given property.
/// </summary>
public readonly struct Change<T>
{
    public T OldValue { get; }
    public T NewValue { get; }

    public Change(T oldValue, T newValue)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }

    public override string ToString() =>
        $"Old: {OldValue}, New: {NewValue}";
}

/// <summary>
/// Represents the precomputed differences for a model update.
/// The dictionary entries are keyed on a property name (string)
/// and store a Change value boxed as an object.
///
/// Note: This class uses pooled dictionaries for efficiency. The dictionary
/// is returned to the pool when Dispose() is called or when the finalizer runs.
/// Since event handlers may be async and fire-and-forget, we rely on the finalizer
/// as backup for handlers that don't explicitly dispose.
/// </summary>
public class ModelChange<TModel> : IDisposable
{
    public static readonly ModelChange<TModel> Empty = 
        new ModelChange<TModel>(null);
    
    private readonly Dictionary<string, object>? _changes;

    public ModelChange(Dictionary<string, object>? changes)
    {
        _changes = changes;
    }

    /// <summary>
    /// Returns a property change result for the property specified
    /// by an expression such as x => x.Property.
    /// </summary>
    public PropertyChange<TProperty, TModel> Check<TProperty>(
        Expression<Func<TModel, TProperty>> propertyExpression)
    {
        if (_changes is null)
            return new PropertyChange<TProperty, TModel>(false, null);
        
        if (propertyExpression.Body is not MemberExpression memberExpr)
            throw new ArgumentException("Expression must be a property access.", 
                nameof(propertyExpression));

        // Use the caching to get the property info if needed.
        if (memberExpr.Member is not PropertyInfo propertyInfo)
            throw new ArgumentException("Expression must be a property access.", 
                nameof(propertyExpression));

        var propertyName = propertyInfo.Name;

        if (_changes.TryGetValue(propertyName, out object? changeObj) &&
            changeObj is Change<TProperty> change)
        {
            return new PropertyChange<TProperty, TModel>(true, change);
        }
        else
        {
            // Property was not found among the changed properties.
            return new PropertyChange<TProperty, TModel>(false, null);
        }
    }
    
    /// <summary>
    /// Checks whether a property changed and (if it did) returns
    /// the old and new values directly via out parameters.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertyExpression">The property expression.</param>
    /// <returns>True if the property was changed; otherwise, false.</returns>
    public bool On<TProperty>(Expression<Func<TModel, TProperty>> propertyExpression)
    {
        var propChg = Check(propertyExpression);
        return (propChg.IsChanged && propChg.Change is not null);
    }
    
    /// <summary>
    /// Checks whether a property changed and (if it did) returns
    /// the old and new values directly via out parameters.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="propertyExpression">The property expression.</param>
    /// <param name="oldValue">Output the old value if changed.</param>
    /// <param name="newValue">Output the new value if changed.</param>
    /// <returns>True if the property was changed; otherwise, false.</returns>
    public bool On<TProperty>(
        Expression<Func<TModel, TProperty>> propertyExpression,
        out TProperty oldValue,
        out TProperty newValue)
    {
        var propChg = Check(propertyExpression);
        if (propChg.IsChanged && propChg.Change is not null)
        {
            oldValue = propChg.Change.Value.OldValue;
            newValue = propChg.Change.Value.NewValue;
            return true;
        }

        oldValue = default!;
        newValue = default!;
        return false;
    }
    
    #region Disposal
    
    private bool _disposed = false;
    
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
            if (_changes != null)
            {
                ModelUpdateUtils.ReturnChangeDict(_changes);
            }
        }
        
        _disposed = true;
    }

    ~ModelChange()
    {
        Dispose(false);
    }
    
    #endregion
}

/// <summary>
/// Represents the result when querying a property change.
/// It indicates whether the property changed and, if yes, contains
/// a strongly typed change.
/// </summary>
public readonly struct PropertyChange<TProperty, TModel>
{
    /// <summary>
    /// Indicates whether the property change was recorded.
    /// </summary>
    public readonly bool IsChanged;

    /// <summary>
    /// The strongly typed change for the property.
    /// </summary>
    public readonly Change<TProperty>? Change;

    public PropertyChange(bool isChanged, Change<TProperty>? change)
    {
        IsChanged = isChanged;
        Change = change;
    }
}

public static class ChangeFactoryCache
{
    // For each property type, store a delegate:
    // (object? oldVal, object? newVal) => object (which is a boxed Change<T>)
    private static readonly ConcurrentDictionary<Type, Func<object?, object?, object>> Cache =
        new ();

    /// <summary>
    /// Obtains a delegate that constructs a Change&lt;T&gt; for the given type.
    /// </summary>
    public static Func<object?, object?, object> GetOrAddFactory(Type propertyType)
    {
        return Cache.GetOrAdd(propertyType, CreateFactory);
    }

    private static Func<object?, object?, object> CreateFactory(Type propertyType)
    {
        // We want to build a delegate that does:
        // (object oldVal, object newVal) => new Change<T>((T)oldVal, (T)newVal)
        var oldParam = Expression.Parameter(typeof(object), "oldVal");
        var newParam = Expression.Parameter(typeof(object), "newVal");

        // Cast the object parameters to the target type.
        var castOld = Expression.Convert(oldParam, propertyType);
        var castNew = Expression.Convert(newParam, propertyType);

        // Create a new Change<T> call:
        var changeType = typeof(Change<>).MakeGenericType(propertyType);
        var ctor = changeType.GetConstructor(new[] { propertyType, propertyType })!;

        var newChange = Expression.New(ctor, castOld, castNew);

        // Box the resulting Change<T> to object.
        var boxedNewChange = Expression.Convert(newChange, typeof(object));

        // Compile the lambda.
        var lambda = Expression.Lambda<Func<object?, object?, object>>(boxedNewChange, oldParam, newParam);
        return lambda.Compile();
    }
}