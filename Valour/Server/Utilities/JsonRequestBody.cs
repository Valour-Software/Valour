using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;

namespace Valour.Server.Utilities;

/// <summary>
/// Binds JSON requests that contain polymorphic values. Unsupported input shapes
/// are exposed to the handler as invalid bodies, including missing type discriminators.
/// </summary>
public sealed class JsonRequestBody<T> : IEndpointParameterMetadataProvider where T : class
{
    public T? Value { get; }

    private JsonRequestBody(T? value) => Value = value;

    public static async ValueTask<JsonRequestBody<T>> BindAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType())
            throw new BadHttpRequestException("Expected an application/json request body.", StatusCodes.Status415UnsupportedMediaType);

        try
        {
            return new(await context.Request.ReadFromJsonAsync<T>(context.RequestAborted));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return new(null);
        }
    }

    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder) =>
        builder.Metadata.Add(new BodyMetadata());

    private sealed class BodyMetadata : IAcceptsMetadata
    {
        public IReadOnlyList<string> ContentTypes { get; } = ["application/json"];
        public Type RequestType => typeof(T);
        public bool IsOptional => false;
    }
}
