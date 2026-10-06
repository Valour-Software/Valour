#nullable enable

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Valour.Sdk.ModelLogic;

/// <summary>
/// Responsible for pushing model updates across the client. Accessors for each
/// model type are built the first time that type is updated, then reused.
/// </summary>
public static class ModelUpdateUtils
{
    private static readonly ConcurrentDictionary<Type, PropertyAccessor[]> Accessors = new();

    /// <summary>
    /// Creates the dictionary that backs a <see cref="ModelChange{TModel}"/>.
    /// Entries map a property name to a boxed <see cref="Change{T}"/>.
    /// </summary>
    public static Dictionary<string, object> CreateChangeDict() => new(2);

    /// <summary>
    /// Copies every realtime-synchronized property of <paramref name="updated"/> onto
    /// <paramref name="existing"/>. Returns the changed properties, or null when nothing differed.
    /// </summary>
    internal static Dictionary<string, object>? CopyChanges(object existing, object updated)
    {
        var accessors = Accessors.GetOrAdd(existing.GetType(), CreateAccessors);

        Dictionary<string, object>? changes = null;
        for (var i = 0; i < accessors.Length; i++)
            accessors[i].Apply(existing, updated, ref changes);

        return changes;
    }

    private static PropertyAccessor[] CreateAccessors(Type modelType)
    {
        var accessors = new List<PropertyAccessor>();

        foreach (var property in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || !property.CanRead ||
                property.GetIndexParameters().Length > 0 ||
                property.GetCustomAttribute<IgnoreRealtimeChangesAttribute>() is not null)
                continue;

            accessors.Add(CreateAccessor(property));
        }

        return accessors.ToArray();
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TypedPropertyAccessor<,>))]
    private static PropertyAccessor CreateAccessor(PropertyInfo property)
    {
        // Platforms that cannot generate code at runtime cannot instantiate the
        // typed accessor over arbitrary value types, so they use reflection.
        if (!RuntimeFeature.IsDynamicCodeSupported)
            return new BoxedPropertyAccessor(property);

        var accessorType = typeof(TypedPropertyAccessor<,>)
            .MakeGenericType(property.DeclaringType!, property.PropertyType);

        return (PropertyAccessor)Activator.CreateInstance(accessorType, property)!;
    }

    private abstract class PropertyAccessor
    {
        public abstract void Apply(object existing, object updated, ref Dictionary<string, object>? changes);
    }

    // Reads and writes the property through typed delegates, so unchanged value-type
    // properties are compared without boxing. Only changed properties allocate.
    private sealed class TypedPropertyAccessor<TOwner, TValue> : PropertyAccessor
        where TOwner : class
    {
        private readonly string _name;
        private readonly Func<TOwner, TValue> _getter;
        private readonly Action<TOwner, TValue> _setter;
        private readonly Func<TValue, TValue, bool> _equals;

        public TypedPropertyAccessor(PropertyInfo property)
        {
            _name = property.Name;
            _getter = (Func<TOwner, TValue>)property.GetMethod!.CreateDelegate(typeof(Func<TOwner, TValue>));
            _setter = (Action<TOwner, TValue>)property.SetMethod!.CreateDelegate(typeof(Action<TOwner, TValue>));

            _equals = typeof(TValue).IsValueType
                ? EqualityComparer<TValue>.Default.Equals
                : static (a, b) => ModelValueComparer.AreEqual(a, b);
        }

        public override void Apply(object existing, object updated, ref Dictionary<string, object>? changes)
        {
            var current = (TOwner)existing;
            var incoming = (TOwner)updated;

            var oldValue = _getter(current);
            var newValue = _getter(incoming);

            if (!_equals(oldValue, newValue))
            {
                changes ??= CreateChangeDict();
                changes[_name] = new Change<TValue>(oldValue, newValue);
            }

            _setter(current, newValue);
        }
    }

    private sealed class BoxedPropertyAccessor : PropertyAccessor
    {
        private readonly PropertyInfo _property;

        public BoxedPropertyAccessor(PropertyInfo property)
        {
            _property = property;
        }

        public override void Apply(object existing, object updated, ref Dictionary<string, object>? changes)
        {
            var oldValue = _property.GetValue(existing);
            var newValue = _property.GetValue(updated);

            if (!ModelValueComparer.AreEqual(oldValue, newValue))
            {
                changes ??= CreateChangeDict();
                changes[_property.Name] = ChangeFactoryCache.GetOrAddFactory(_property.PropertyType)(oldValue, newValue);
            }

            _property.SetValue(existing, newValue);
        }
    }
}

/// <summary>
/// Decides whether two synchronized property values are the same. Byte arrays and
/// lists of primitives compare by content so that re-syncing an unchanged message
/// does not look like an edit. Other reference types use their own equality.
/// </summary>
internal static class ModelValueComparer
{
    public static bool AreEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a is null || b is null)
            return false;

        switch (a)
        {
            case byte[] bytes:
                return b is byte[] otherBytes && bytes.AsSpan().SequenceEqual(otherBytes);
            case int[] ints:
                return b is int[] otherInts && ints.AsSpan().SequenceEqual(otherInts);
            case long[] longs:
                return b is long[] otherLongs && longs.AsSpan().SequenceEqual(otherLongs);
            case List<long> longList:
                return b is List<long> otherLongList &&
                       CollectionsMarshal.AsSpan(longList).SequenceEqual(CollectionsMarshal.AsSpan(otherLongList));
            case List<int> intList:
                return b is List<int> otherIntList &&
                       CollectionsMarshal.AsSpan(intList).SequenceEqual(CollectionsMarshal.AsSpan(otherIntList));
            case List<string> stringList:
                return b is List<string> otherStringList && StringListsEqual(stringList, otherStringList);
            default:
                return a.Equals(b);
        }
    }

    private static bool StringListsEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i]))
                return false;
        }

        return true;
    }
}
