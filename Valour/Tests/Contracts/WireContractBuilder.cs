using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Valour.Sdk.ModelLogic;
using Valour.Server;
using Valour.Server.Hubs;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Tests.Contracts;

/// <summary>
/// Produces the <see cref="WireContract"/> of the code under test. It builds the
/// server's routes the way <c>Program.Main</c> does, but without applying
/// migrations, connecting to Redis, loading settings files, or starting the
/// host, so it needs no database and touches no outside service.
/// </summary>
public static partial class WireContractBuilder
{
    public static async Task<WireContract> BuildAsync()
    {
        var types = new WireTypeCatalog();
        var contract = new WireContract
        {
            Version = typeof(ISharedUser).Assembly.GetName().Version?.ToString(3),
            ClientProtocol = InstanceManifest.CurrentClientProtocol,
            OldestSupportedClientProtocol = InstanceManifest.OldestSupportedClientProtocol,
        };

        foreach (var (key, endpoint) in await ReadEndpointsAsync(types))
            contract.Endpoints.Add(key, endpoint);

        foreach (var method in typeof(CoreHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            // Connection lifecycle overrides are not methods clients invoke.
            if (method.IsSpecialName || method.GetBaseDefinition().DeclaringType != typeof(CoreHub))
                continue;

            contract.HubMethods.Add(method.Name, new WireHubMethod
            {
                Parameters = method.GetParameters().Select(p => types.Describe(p.ParameterType)).ToList(),
                Returns = types.Describe(method.ReturnType),
            });
        }

        foreach (var field in typeof(HubEvents).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral && field.FieldType == typeof(string))
                contract.Events.Add((string)field.GetRawConstantValue()!);
        }

        foreach (var model in GetRealtimeModelTypes())
        {
            contract.Events.Add(HubEvents.ModelUpdate(model.Name));
            contract.Events.Add(HubEvents.ModelDelete(model.Name));
            types.Describe(model);
        }

        foreach (var type in GetSharedWireTypes())
            types.Describe(type);

        contract.Types = types.Build();
        return contract;
    }

    /// <summary>
    /// Server models the SDK has a client model for. The server announces their
    /// changes as "{Name}-Update" and "{Name}-Delete", and the SDK listens for
    /// each client model by its own name, so these names are part of the contract.
    /// The same models make up most API responses.
    /// </summary>
    private static IEnumerable<Type> GetRealtimeModelTypes()
    {
        var clientModelNames = typeof(ClientModel<>).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true } && IsClientModel(t))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        return typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsAbstract: false, IsGenericTypeDefinition: false } &&
                        t.Namespace?.StartsWith("Valour.Server.Models", StringComparison.Ordinal) == true &&
                        clientModelNames.Contains(t.Name))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
    }

    private static bool IsClientModel(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ClientModel<>))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Request, response, and event types defined in the shared library and the
    /// SDK wire-format namespaces the server uses. API handlers return
    /// <c>Results.Json</c>, which carries no response type, so these types are
    /// recorded directly instead of being found through the routes.
    /// </summary>
    private static IEnumerable<Type> GetSharedWireTypes()
    {
        string[] sdkWireNamespaces = ["Valour.Sdk.E2ee", "Valour.Sdk.Models.Embeds"];

        var shared = typeof(ISharedUser).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Valour.Shared.Models", StringComparison.Ordinal) == true);

        var sdk = typeof(ClientModel<>).Assembly.GetTypes()
            .Where(t => sdkWireNamespaces.Any(ns =>
                t.Namespace == ns || t.Namespace?.StartsWith(ns + ".", StringComparison.Ordinal) == true));

        return shared.Concat(sdk)
            .Where(IsDataType)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
    }

    private static bool IsDataType(Type type)
    {
        if (!type.IsPublic && !type.IsNestedPublic)
            return false;

        if (type.IsEnum)
            return true;

        if (type.IsInterface || type.IsGenericTypeDefinition || (type.IsAbstract && type.IsSealed) ||
            typeof(Attribute).IsAssignableFrom(type) || typeof(Exception).IsAssignableFrom(type) ||
            typeof(Delegate).IsAssignableFrom(type))
            return false;

        // Data types have properties a deserializer can set. Classes whose
        // properties are all read-only hold behavior, such as readers and stores.
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetSetMethod() is not null);
    }

    private static async Task<List<(string Key, WireEndpoint Endpoint)>> ReadEndpointsAsync(WireTypeCatalog types)
    {
        using var configs = ConfigSingletons.UseDefaultsWhereMissing();

        // An empty content root keeps the host from reading a local settings
        // file, which may hold credentials for live services.
        var contentRoot = Directory.CreateTempSubdirectory("valour-wire-contract");
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ContentRootPath = contentRoot.FullName,
                ApplicationName = typeof(Program).Assembly.GetName().Name,
            });

            Program.ConfigureServices(builder, connectInfrastructure: false);
            await using var app = builder.Build();
            Program.MapApiRoutes(app, servePublicContent: true);

            // Registers the mapped routes with the endpoint data source that the
            // API explorer reads. The host is never started.
            app.UseRouting();
            app.UseEndpoints(_ => { });

            var descriptions = app.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
                .ApiDescriptionGroups.Items
                .SelectMany(group => group.Items);

            var endpoints = new List<(string, WireEndpoint)>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var description in descriptions)
            {
                var (key, endpoint) = DescribeEndpoint(description, types);
                if (!keys.Add(key))
                    throw new InvalidOperationException($"Two routes normalize to \"{key}\". Routes must differ by more than parameter names.");

                endpoints.Add((key, endpoint));
            }

            return endpoints;
        }
        finally
        {
            contentRoot.Delete(recursive: true);
        }
    }

    private static (string Key, WireEndpoint Endpoint) DescribeEndpoint(ApiDescription description, WireTypeCatalog types)
    {
        var path = description.RelativePath ?? string.Empty;
        var endpoint = new WireEndpoint();

        foreach (Match token in RouteToken().Matches(path))
        {
            var name = token.Groups["name"].Value;
            var parameter = description.ParameterDescriptions.FirstOrDefault(p =>
                p.Source == BindingSource.Path && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            endpoint.Path.Add(parameter?.Type is null ? "string" : types.Describe(parameter.Type));
        }

        foreach (var parameter in description.ParameterDescriptions)
        {
            if (parameter.Source == BindingSource.Path || parameter.Type is null)
                continue;

            var wire = new WireParameter { Type = types.Describe(parameter.Type), Required = parameter.IsRequired };
            if (parameter.Source == BindingSource.Body)
            {
                endpoint.Body = wire;
                continue;
            }

            var source = parameter.Source == BindingSource.Query ? "query"
                : parameter.Source == BindingSource.Header ? "header"
                : parameter.Source == BindingSource.Form || parameter.Source == BindingSource.FormFile ? "form"
                : null;

            // Services and values such as HttpContext do not come from the request.
            if (source is null)
                continue;

            endpoint.Parameters[$"{source} {parameter.Name.ToLowerInvariant()}"] = wire;
        }

        var normalizedPath = RouteToken().Replace(path.Trim('/'), match =>
        {
            var constraint = match.Groups["constraint"].Value;
            var marker = match.Groups["catchAll"].Success ? "*" : string.Empty;
            var optional = match.Groups["optional"].Success ? "?" : string.Empty;
            return "{" + marker + constraint.TrimStart(':') + optional + "}";
        });

        // Routing ignores case, so a change of case is not a change of route.
        normalizedPath = LiteralSegment().Replace(normalizedPath, m => m.Value.ToLowerInvariant());

        return ($"{description.HttpMethod ?? "ANY"} {normalizedPath}", endpoint);
    }

    [GeneratedRegex(@"\{(?<catchAll>\*{1,2})?(?<name>[^{}:?=*]+)(?<constraint>(?::[^{}?=]+)*)(?<optional>\?)?(?:=[^{}]*)?\}")]
    private static partial Regex RouteToken();

    [GeneratedRegex(@"(?<=^|/)[^/{}]+(?=/|$)")]
    private static partial Regex LiteralSegment();
}

/// <summary>
/// Collects the JSON shape of every named type the contract mentions, using the
/// web serializer defaults that minimal API responses and the SignalR JSON
/// protocol use: camel-case names and numeric enums unless a type says otherwise.
/// </summary>
internal sealed class WireTypeCatalog
{
    private static readonly Dictionary<Type, string> Primitives = new()
    {
        [typeof(string)] = "string",
        [typeof(char)] = "string",
        [typeof(bool)] = "boolean",
        [typeof(byte)] = "uint8",
        [typeof(sbyte)] = "int8",
        [typeof(short)] = "int16",
        [typeof(ushort)] = "uint16",
        [typeof(int)] = "int32",
        [typeof(uint)] = "uint32",
        [typeof(long)] = "int64",
        [typeof(ulong)] = "uint64",
        [typeof(float)] = "float32",
        [typeof(double)] = "float64",
        [typeof(decimal)] = "decimal",
        [typeof(DateTime)] = "datetime",
        [typeof(DateTimeOffset)] = "datetimeoffset",
        [typeof(DateOnly)] = "date",
        [typeof(TimeOnly)] = "time",
        [typeof(TimeSpan)] = "duration",
        [typeof(Guid)] = "guid",
        [typeof(Uri)] = "uri",
        [typeof(System.Version)] = "version",
        [typeof(byte[])] = "bytes",
        [typeof(object)] = "any",
        [typeof(JsonElement)] = "any",
        [typeof(JsonDocument)] = "any",
    };

    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private readonly Dictionary<Type, string> _names = new();
    private readonly Queue<Type> _pending = new();

    /// <summary>
    /// Returns how a value of the type appears on the wire, such as "int64?",
    /// "array&lt;string&gt;", or the name of an object type, and queues named types
    /// so that <see cref="Build"/> records their shape.
    /// </summary>
    public string Describe(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Describe(underlying) + "?";

        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
            return "void";

        if (type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
            (definition == typeof(Task<>) || definition == typeof(ValueTask<>)))
            return Describe(type.GetGenericArguments()[0]);

        if (Primitives.TryGetValue(type, out var primitive))
            return primitive;

        if (typeof(JsonNode).IsAssignableFrom(type))
            return "any";

        if (typeof(IResult).IsAssignableFrom(type))
            return "result";

        if (typeof(IFormFileCollection).IsAssignableFrom(type))
            return "array<file>";

        if (typeof(IFormFile).IsAssignableFrom(type))
            return "file";

        if (type.IsEnum)
            return "enum " + Name(type);

        JsonTypeInfo info;
        try
        {
            info = _options.GetTypeInfo(type);
        }
        catch (Exception)
        {
            // The serializer cannot write this type at all; record it by name.
            return Name(type);
        }

        return info.Kind switch
        {
            JsonTypeInfoKind.Enumerable => $"array<{Describe(info.ElementType!)}>",
            JsonTypeInfoKind.Dictionary => $"map<{Describe(info.KeyType!)}, {Describe(info.ElementType!)}>",
            _ => Name(type),
        };
    }

    public SortedDictionary<string, WireType> Build()
    {
        var shapes = new SortedDictionary<string, WireType>(StringComparer.Ordinal);
        while (_pending.TryDequeue(out var type))
            shapes[_names[type]] = DescribeShape(type);

        return shapes;
    }

    private string Name(Type type)
    {
        if (_names.TryGetValue(type, out var known))
            return known;

        var simple = type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name;
        var owner = type.IsNested ? Name(type.DeclaringType!) : type.Namespace;
        var name = owner is null ? simple : owner + "." + simple;
        if (type.IsGenericType)
            name += "<" + string.Join(", ", type.GetGenericArguments().Select(Describe)) + ">";

        _names[type] = name;
        _pending.Enqueue(type);
        return name;
    }

    private WireType DescribeShape(Type type)
    {
        if (type.IsEnum)
        {
            var values = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                values[field.Name] = Convert.ToInt64(field.GetRawConstantValue());

            return new WireType
            {
                Kind = "enum",
                Format = WritesEnumAsString(type) ? "string" : "number",
                Values = values,
            };
        }

        JsonTypeInfo info;
        try
        {
            info = _options.GetTypeInfo(type);
        }
        catch (Exception)
        {
            return new WireType { Kind = "custom" };
        }

        if (info.Kind != JsonTypeInfoKind.Object)
            return new WireType { Kind = "custom" };

        var shape = new WireType
        {
            Kind = "object",
            Properties = new SortedDictionary<string, string>(StringComparer.Ordinal),
        };

        foreach (var property in info.Properties)
        {
            // [JsonIgnore] leaves the property in the metadata with neither
            // accessor, and it never appears in JSON.
            if (property.Get is null && property.Set is null)
                continue;

            shape.Properties[property.Name] = Describe(property.PropertyType);
        }

        if (info.PolymorphismOptions is { } polymorphism)
        {
            shape.Discriminator = polymorphism.TypeDiscriminatorPropertyName;
            shape.DerivedTypes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var derived in polymorphism.DerivedTypes)
                shape.DerivedTypes[derived.TypeDiscriminator?.ToString() ?? string.Empty] = Describe(derived.DerivedType);
        }

        return shape;
    }

    private static bool WritesEnumAsString(Type type)
    {
        var converter = type.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;
        return converter is not null &&
               (converter == typeof(JsonStringEnumConverter) ||
                (converter.IsGenericType && converter.GetGenericTypeDefinition() == typeof(JsonStringEnumConverter<>)));
    }
}

/// <summary>
/// The server reads settings through static singletons such as
/// <c>HostingConfig.Current</c>, which <c>ConfigLoader</c> fills from the
/// settings file. The contract needs no settings, so missing singletons get
/// default instances while the routes are built, and are cleared afterwards so
/// other tests in the same run see what they would have seen.
/// </summary>
internal sealed class ConfigSingletons : IDisposable
{
    private readonly List<Action> _restore = [];

    public static ConfigSingletons UseDefaultsWhereMissing()
    {
        var scope = new ConfigSingletons();
        var configTypes = typeof(Valour.Config.Configs.HostingConfig).Assembly.GetTypes()
            .Where(t => t.Namespace == "Valour.Config.Configs" && t is { IsClass: true, IsAbstract: false });

        foreach (var type in configTypes)
        {
            foreach (var name in new[] { "Current", "Instance" })
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                if (type.GetProperty(name, flags) is { } property && property.PropertyType == type)
                {
                    if (property.GetValue(null) is not null)
                        continue;

                    Activator.CreateInstance(type);
                    var setter = property.GetSetMethod(nonPublic: true);
                    if (setter is not null)
                        scope._restore.Add(() => setter.Invoke(null, [null]));
                }
                else if (type.GetField(name, flags) is { } field && field.FieldType == type)
                {
                    if (field.GetValue(null) is not null)
                        continue;

                    Activator.CreateInstance(type);
                    scope._restore.Add(() => field.SetValue(null, null));
                }
            }
        }

        return scope;
    }

    public void Dispose()
    {
        foreach (var restore in _restore)
            restore();
    }
}
