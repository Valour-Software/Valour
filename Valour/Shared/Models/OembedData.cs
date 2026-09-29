using System.Text.Json;
using System.Text.Json.Serialization;

namespace Valour.Shared.Models;

public class OembedData
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("version")]
    [JsonConverter(typeof(LenientStringConverter))]
    public string Version { get; set; }

    [JsonPropertyName("width")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int? Height { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("author_name")]
    public string AuthorName { get; set; }

    [JsonPropertyName("author_url")]
    public string AuthorUrl { get; set; }

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; }

    [JsonPropertyName("provider_url")]
    public string ProviderUrl { get; set; }

    [JsonPropertyName("cache_age")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int? CacheAge { get; set; }

    [JsonPropertyName("html")]
    public string Html { get; set; }

    /// <summary>
    /// Some providers send "version" as a number instead of a string.
    /// </summary>
    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                    return reader.TryGetDouble(out var number)
                        ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : null;
                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value);
        }
    }

    /// <summary>
    /// Providers disagree on number fields: some send numbers, some numeric
    /// strings, and some values like "100%". Anything that is not a whole
    /// number reads as absent instead of failing the whole response.
    /// </summary>
    private sealed class LenientIntConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out var number))
                        return number;
                    return reader.TryGetDouble(out var real) && real is >= int.MinValue and <= int.MaxValue
                        ? (int)Math.Round(real)
                        : null;
                case JsonTokenType.String:
                    return int.TryParse(reader.GetString(), out var parsed) ? parsed : null;
                default:
                    reader.Skip();
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value is null)
                writer.WriteNullValue();
            else
                writer.WriteNumberValue(value.Value);
        }
    }
}
