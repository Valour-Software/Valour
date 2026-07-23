using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Valour.Sdk.Models;
using Valour.Sdk.Models.Embeds;
using Valour.Shared;

namespace Valour.Sdk.Client;

/// <summary>
/// A lightweight client for executing a Valour incoming webhook. Needs only
/// the webhook URL — no account, token, or ValourClient required — so bots
/// and scripts can post with a plain HttpClient.
/// </summary>
/// <example>
/// var webhook = new WebhookClient("https://app.valour.gg/api/webhooks/123/whk_abc");
/// await webhook.SendAsync("Build passed ✅");
/// await webhook.SendAsync(new WebhookExecuteRequest
/// {
///     Content = "Deploy finished",
///     OverrideName = "CI",
/// }.WithEmbed(new EmbedBuilder().AddPage("Deploy").AddText("All green.").Build()));
/// </example>
public class WebhookClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    /// <param name="webhookUrl">The full execute URL: .../api/webhooks/{id}/{token}</param>
    /// <param name="http">Optional HttpClient to use; one is created if omitted.</param>
    public WebhookClient(string webhookUrl, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            throw new ArgumentException("Webhook URL is required.", nameof(webhookUrl));

        _baseUrl = webhookUrl.TrimEnd('/');
        _http = http ?? new HttpClient();
    }

    /// <summary>
    /// Sends a plain text message through the webhook.
    /// </summary>
    public Task<TaskResult<Message>> SendAsync(string content) =>
        SendAsync(new WebhookExecuteRequest { Content = content });

    /// <summary>
    /// Sends an embed (with optional text) through the webhook.
    /// </summary>
    public Task<TaskResult<Message>> SendAsync(Embed embed, string? content = null) =>
        SendAsync(new WebhookExecuteRequest { Content = content }.WithEmbed(embed));

    /// <summary>
    /// Sends a fully specified message through the webhook.
    /// </summary>
    public async Task<TaskResult<Message>> SendAsync(WebhookExecuteRequest request)
    {
        var response = await _http.PostAsJsonAsync(_baseUrl, request, JsonOptions);
        return await ReadResultAsync<Message>(response);
    }

    /// <summary>
    /// Edits a message previously sent by this webhook.
    /// </summary>
    public async Task<TaskResult<Message>> EditMessageAsync(long messageId, WebhookMessageEditRequest request)
    {
        var response = await _http.PutAsJsonAsync($"{_baseUrl}/messages/{messageId}", request, JsonOptions);
        return await ReadResultAsync<Message>(response);
    }

    /// <summary>
    /// Deletes a message previously sent by this webhook.
    /// </summary>
    public async Task<TaskResult> DeleteMessageAsync(long messageId)
    {
        var response = await _http.DeleteAsync($"{_baseUrl}/messages/{messageId}");
        if (response.IsSuccessStatusCode)
            return TaskResult.SuccessResult;

        return TaskResult.FromFailure(await DescribeErrorAsync(response));
    }

    /// <summary>
    /// Fetches the webhook's public info (name, avatar, channel).
    /// </summary>
    public async Task<TaskResult<PlanetWebhook>> GetInfoAsync()
    {
        var response = await _http.GetAsync(_baseUrl);
        return await ReadResultAsync<PlanetWebhook>(response);
    }

    private static async Task<TaskResult<T>> ReadResultAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return TaskResult<T>.FromFailure(await DescribeErrorAsync(response));

        try
        {
            var data = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
            return new TaskResult<T>(true, "Success", data);
        }
        catch (JsonException)
        {
            return TaskResult<T>.FromFailure("Failed to parse server response.");
        }
    }

    private static async Task<string> DescribeErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == (HttpStatusCode)429)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            return retryAfter is null
                ? "Rate limited."
                : $"Rate limited. Retry after {retryAfter} seconds.";
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"Request failed with status {(int)response.StatusCode}."
            : body;
    }
}
