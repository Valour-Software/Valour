using System.Reflection;
using Microsoft.AspNetCore.Components;
using Valour.Client.Components.Staff;

namespace Valour.Tests.Client;

public class ComponentParameterUniquenessTests
{
    [Fact]
    public void ClientComponents_DoNotRedeclareCaseInsensitiveParameterNames()
    {
        var failures = typeof(StaffUserToolsModal).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type))
            .Select(type => new
            {
                Type = type,
                Duplicates = GetDeclaredComponentParameters(type)
                    .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .ToArray(),
            })
            .Where(result => result.Duplicates.Length > 0)
            .Select(result => $"{result.Type.FullName}: {string.Join(", ", result.Duplicates)}")
            .ToArray();

        Assert.Empty(failures);
    }

    private static IEnumerable<PropertyInfo> GetDeclaredComponentParameters(Type componentType)
    {
        for (var type = componentType; type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (property.IsDefined(typeof(ParameterAttribute), inherit: false)
                    || property.IsDefined(typeof(CascadingParameterAttribute), inherit: false))
                {
                    yield return property;
                }
            }
        }
    }
}
