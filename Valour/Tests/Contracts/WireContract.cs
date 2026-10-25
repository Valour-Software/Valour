using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Valour.Tests.Contracts;

/// <summary>
/// What a server build exposes to clients: its HTTP routes, the hub methods
/// clients call, the realtime events it sends, and the JSON shape of the types
/// those carry. <see cref="WireContractComparer"/> compares the contract of the
/// current code with the one recorded for the oldest supported release.
/// </summary>
public sealed class WireContract
{
    /// <summary>The release version of the build that produced this contract.</summary>
    public string Version { get; set; }

    /// <summary>The client protocol the build speaks.</summary>
    public int ClientProtocol { get; set; }

    /// <summary>The oldest client protocol the build serves.</summary>
    public int OldestSupportedClientProtocol { get; set; }

    /// <summary>HTTP routes, keyed by method and path, such as "GET api/channels/{long}".</summary>
    public SortedDictionary<string, WireEndpoint> Endpoints { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Core hub methods clients invoke, keyed by name.</summary>
    public SortedDictionary<string, WireHubMethod> HubMethods { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Names of the events the server sends over the core hub.</summary>
    public SortedSet<string> Events { get; set; } = new(StringComparer.Ordinal);

    /// <summary>JSON shapes of the types used above, keyed by type name.</summary>
    public SortedDictionary<string, WireType> Types { get; set; } = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keeps generic type names such as array<int64> readable in the file.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, FileOptions) + "\n";

    public static WireContract FromJson(string json) =>
        JsonSerializer.Deserialize<WireContract>(json, FileOptions)
        ?? throw new InvalidDataException("The wire contract file is empty.");
}

public sealed class WireEndpoint
{
    /// <summary>Types of the route parameters, in the order they appear in the path.</summary>
    public List<string> Path { get; set; } = [];

    /// <summary>Query, header, and form parameters, keyed by "source name" in lowercase.</summary>
    public SortedDictionary<string, WireParameter> Parameters { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The JSON request body, if the route reads one.</summary>
    public WireParameter Body { get; set; }
}

public sealed class WireParameter
{
    public string Type { get; set; }
    public bool Required { get; set; }
}

public sealed class WireHubMethod
{
    public List<string> Parameters { get; set; } = [];
    public string Returns { get; set; }
}

public sealed class WireType
{
    /// <summary>"object", "enum", or "custom" for a type with its own JSON converter.</summary>
    public string Kind { get; set; }

    /// <summary>Object properties: JSON name to type.</summary>
    public SortedDictionary<string, string> Properties { get; set; }

    /// <summary>For enums, "number" or "string", matching how values are written.</summary>
    public string Format { get; set; }

    /// <summary>Enum members: name to numeric value.</summary>
    public SortedDictionary<string, long> Values { get; set; }

    /// <summary>For polymorphic types, the JSON property naming the concrete type.</summary>
    public string Discriminator { get; set; }

    /// <summary>For polymorphic types, discriminator value to concrete type.</summary>
    public SortedDictionary<string, string> DerivedTypes { get; set; }
}
