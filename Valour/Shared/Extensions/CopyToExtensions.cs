namespace Valour.Shared.Extensions;

public static class CopyToExtension
{
    public static void CopyAllTo<T>(this T source, T target)
    {
        var type = typeof(T);
        foreach (var sourceProperty in type.GetProperties())
        {
            var targetProperty = type.GetProperty(sourceProperty.Name);
            if (targetProperty.CanWrite)
                targetProperty.SetValue(target, sourceProperty.GetValue(source, null), null);
        }
        foreach (var sourceField in type.GetFields())
        {
            var targetField = type.GetField(sourceField.Name);
            if (!targetField.IsStatic)
                targetField.SetValue(target, sourceField.GetValue(source));
        }
    }
    
    public static void CopyAllNonDefaultTo<T>(this T source, T target)
    {
        var type = typeof(T);
        foreach (var sourceProperty in type.GetProperties())
        {
            var targetProperty = type.GetProperty(sourceProperty.Name);
            if (targetProperty.CanWrite)
            {
                var sourceValue = sourceProperty.GetValue(source, null);
                if (sourceValue != null && !IsDefaultValue(sourceValue))
                {
                    targetProperty.SetValue(target, sourceValue, null);
                }
            }
        }
        foreach (var sourceField in type.GetFields())
        {
            var targetField = type.GetField(sourceField.Name);
            if (!targetField.IsStatic)
            {
                var sourceValue = sourceField.GetValue(source);
                if (sourceValue != null && !IsDefaultValue(sourceValue))
                {
                    targetField.SetValue(target, sourceValue);
                }
            }
        }
    }

    private static bool IsDefaultValue<T>(T value)
    {
        return EqualityComparer<T>.Default.Equals(value, default(T));
    }
}