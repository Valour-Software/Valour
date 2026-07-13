using System.Text;
using System.Text.Json;
using Valour.Sdk.Client;

namespace Valour.Sdk.Services;

public class TranslationResult
{
    public string TranslatedText;

    /// <summary>
    /// The language code Google detected the source text as being written in. Null if
    /// the response didn't include one.
    /// </summary>
    public string DetectedLanguage;
}

/// <summary>
/// Translates message text using Google's free, unofficial translate endpoint. This
/// endpoint requires no API key, but is undocumented and could change or be rate
/// limited by Google without notice.
/// </summary>
public class TranslationService : ServiceBase
{
    private const string EndpointBase = "https://translate.googleapis.com/translate_a/single";

    private readonly HttpClient _httpClient;

    private readonly LogOptions _logOptions = new(
        "TranslationService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );

    public TranslationService(HttpClient httpClient, ValourClient client)
    {
        SetupLogging(client.Logger, _logOptions);
        _httpClient = httpClient;
    }

    /// <summary>
    /// Translates the given text into the target language (e.g. "en"). Returns null if
    /// the request fails.
    /// </summary>
    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var url = $"{EndpointBase}?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}&dt=t&q={Uri.EscapeDataString(text)}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                LogError($"Translate request failed ({(int)response.StatusCode} {response.StatusCode})");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            // Response shape: [[[translatedSegment, originalSegment, null, null, ...], ...], null, "detectedLangCode", ...]
            var segments = root[0];
            var translated = new StringBuilder();
            foreach (var segment in segments.EnumerateArray())
            {
                var piece = segment[0].GetString();
                if (piece is not null)
                    translated.Append(piece);
            }

            string detectedLanguage = null;
            if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
                detectedLanguage = root[2].GetString();

            return new TranslationResult
            {
                TranslatedText = translated.ToString(),
                DetectedLanguage = detectedLanguage
            };
        }
        catch (Exception e)
        {
            LogError("Error translating message", e);
            return null;
        }
    }
}
