using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.ObjectPool;

namespace Valour.Sdk.ModelLogic;

/// <summary>
/// Responsible for pushing model updates across the client.
/// Caches reflection metadata to avoid runtime reflection overhead.
/// </summary>
public static class ModelUpdateUtils
{
    // Cache for type properties.
    // The properties of models are cached to avoid reflection overhead,
    // since they won't change during runtime.
    public static readonly ImmutableDictionary<Type, PropertyInfo[]> ModelPropertyCache;

    // Same for fields.
    public static readonly ImmutableDictionary<Type, FieldInfo[]> ModelFieldCache;

    // Cache for compiled property getters.
    // Each entry maps a model type to an array of delegates,
    // with each delegate being a compiled getter:
    // instance => (object)instance.Property.
    public static readonly ImmutableDictionary<Type, Func<object, object>[]>
        ModelGetterCache;

    // Cache for compiled property setters.
    // Each entry maps a model type to an array of delegates,
    // with each delegate being a compiled setter:
    // (instance, value) => instance.Property = (PropertyType)value.
    public static readonly ImmutableDictionary<Type, Action<object, object>[]>
        ModelSetterCache;
    
    public static readonly ObjectPool<Dictionary<string, object>> ChangeDictPool =
        new DefaultObjectPoolProvider().Create(new DictPooledObjectPolicy<string, object>());

    static ModelUpdateUtils()
    {
        var propertyCache = new Dictionary<Type, PropertyInfo[]>();
        var fieldCache = new Dictionary<Type, FieldInfo[]>();
        var getterCache = new Dictionary<Type, Func<object, object>[]>();
        var setterCache = new Dictionary<Type, Action<object, object>[]>();

        // Cache all properties of all models.
        foreach (var modelType in typeof(ClientModel).Assembly.GetTypes())
        {
            if (modelType.IsSubclassOf(typeof(ClientModel)))
            {
                // Cache properties that are writable and not marked to ignore.
                var props = modelType.GetProperties()
                    .Where(x => x.GetCustomAttribute<IgnoreRealtimeChangesAttribute>() is null &&
                                x.CanWrite)
                    .ToArray();

                propertyCache[modelType] = props;

                // Cache fields that are not marked to ignore.
                var fields = modelType.GetFields()
                    .Where(x => x.GetCustomAttribute<IgnoreRealtimeChangesAttribute>() is null)
                    .ToArray();
                fieldCache[modelType] = fields;

                // Create a compiled getter for each property in the cached properties.
                // (The order of getters and setters will be the same as in the properties array.)
                getterCache[modelType] = props.Select(CreateGetter).ToArray();

                // Create a compiled setter for each property.
                setterCache[modelType] = props.Select(CreateSetter).ToArray();
            }
        }

        ModelPropertyCache = propertyCache.ToImmutableDictionary();
        ModelFieldCache = fieldCache.ToImmutableDictionary();
        ModelGetterCache = getterCache.ToImmutableDictionary();
        ModelSetterCache = setterCache.ToImmutableDictionary();
    }

    /// <summary>
    /// Returns the compiled property getters for the specified model instance.
    /// The array of getters corresponds to the properties in ModelPropertyCache.
    /// </summary>
    /// <param name="model">The model instance.</param>
    /// <returns>An array of getter delegates.</returns>
    public static Func<object, object>[] GetPropertyGetters(object model)
    {
        var type = model.GetType();
        if (ModelGetterCache.TryGetValue(type, out var getters))
        {
            return getters;
        }

        throw new InvalidOperationException(
            $"No getters cached for type {type.FullName}");
    }

    /// <summary>
    /// Returns the compiled property setters for the specified model instance.
    /// The array of setters corresponds to the properties in ModelPropertyCache.
    /// </summary>
    /// <param name="model">The model instance.</param>
    /// <returns>An array of setter delegates.</returns>
    public static Action<object, object>[] GetPropertySetters(object model)
    {
        var type = model.GetType();
        if (ModelSetterCache.TryGetValue(type, out var setters))
        {
            return setters;
        }

        throw new InvalidOperationException(
            $"No setters cached for type {type.FullName}");
    }

    /// <summary>
    /// Creates a compiled getter delegate for a given property.
    /// The resulting delegate takes an object (instance) and returns the property
    /// value as an object.
    /// </summary>
    /// <param name="propertyInfo">The property info.</param>
    /// <returns>A delegate for fast property access.</returns>
    private static Func<object, object> CreateGetter(PropertyInfo propertyInfo)
    {
        // Parameter expression representing the instance (of type object).
        var instanceParam = Expression.Parameter(typeof(object), "instance");

        // Convert the input object to the declaring type of the property.
        var castInstance = Expression.Convert(instanceParam, propertyInfo.DeclaringType!);

        // Create an expression to access the property.
        var propertyAccess = Expression.Property(castInstance, propertyInfo);

        // Box the property value as object.
        var castResult = Expression.Convert(propertyAccess, typeof(object));

        // Compile lambda: instance => (object)((T)instance).Property.
        return Expression.Lambda<Func<object, object>>(castResult, instanceParam)
            .Compile();
    }

    /// <summary>
    /// Creates a compiled setter delegate for a given property.
    /// The resulting delegate takes an object (instance) and an object (value)
    /// and sets the property on the instance.
    /// </summary>
    /// <param name="propertyInfo">The property info.</param>
    /// <returns>A delegate for fast property setting.</returns>
    private static Action<object, object> CreateSetter(PropertyInfo propertyInfo)
    {
        // instance parameter of type object.
        var instanceParam = Expression.Parameter(typeof(object), "instance");

        // value parameter of type object.
        var valueParam = Expression.Parameter(typeof(object), "value");

        // Convert the instance to the declaring type.
        var castInstance = Expression.Convert(instanceParam, propertyInfo.DeclaringType!);

        // Convert the value parameter to the property type.
        var castValue = Expression.Convert(valueParam, propertyInfo.PropertyType);

        // Create a property assignment expression.
        var propertySetter = Expression.Assign(
            Expression.Property(castInstance, propertyInfo), castValue);

        // Compile lambda: (instance, value) => ((T)instance).Property = (PropertyType)value.
        return Expression.Lambda<Action<object, object>>(propertySetter,
            instanceParam, valueParam)
            .Compile();
    }

    /// <summary>
    /// Returns the given hashset back to the pool.
    /// </summary>
    public static void ReturnChangeDict(Dictionary<string, object> changes)
    {
        ChangeDictPool.Return(changes);
    }
}

public class DictPooledObjectPolicy<TKey, TValue> : PooledObjectPolicy<Dictionary<TKey, TValue>>
{
    public override Dictionary<TKey, TValue> Create() => new();

    public override bool Return(Dictionary<TKey, TValue> obj)
    {
        obj.Clear();
        return true;
    }
}