using System;
using System.Net.Http.Json;
using System.Text.Json;
using Valour.Api.Planets;
using Valour.Api.Users;
using Valour.Shared;

namespace Valour.Api.Client;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

public static class ValourClient
{
    /// <summary>
    /// The user for this client instance
    /// </summary>
    public static User Self { get; set; }

    /// <summary>
    /// The token for this client instance
    /// </summary>
    public static string Token => _token;

    /// <summary>
    /// The internal token for this client
    /// </summary>
    private static string _token;

    /// <summary>
    /// The planets this client has joined
    /// </summary>
    public static List<Planet> JoinedPlanets { get; }

    /// <summary>
    /// The HttpClient to be used for connections
    /// </summary>
    public static HttpClient Http => _httpClient;

    /// <summary>
    /// The internal HttpClient
    /// </summary>
    private static HttpClient _httpClient;

    /// <summary>
    /// Gets a json resource from the given uri and deserializes it
    /// </summary>
    public static async Task<TaskResult<T>> GetJsonAsync<T>(string uri)
    {
        var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);

        TaskResult<T> result = new TaskResult<T>()
        {
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            var message = await response.Content.ReadAsStringAsync();

            Console.WriteLine("-----------------------------------------\n" +
                              "Failed GET response for the following:\n" +
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {message}\n" +
                              $"-----------------------------------------");

            result.Message = message;
        }
        else
        {
            result.Data = await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync());
        }

        return result;
    }

    /// <summary>
    /// Puts a string resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PutAsync(string uri, string content)
    {
        StringContent stringContent = new StringContent(content);

        var response = await Http.PutAsync(uri, stringContent);

        TaskResult result = new()
        {
            Message = await response.Content.ReadAsStringAsync(),
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed PUT response for the following:\n" +
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");
        }

        return result;
    }

    /// <summary>
    /// Puts a json resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> PutAsync(string uri, object content)
    {
        JsonContent jsonContent = JsonContent.Create(content);

        var response = await Http.PutAsync(uri, jsonContent);

        TaskResult result = new()
        {
            Message = await response.Content.ReadAsStringAsync(),
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed PUT response for the following:\n" +
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");
        }

        return result;
    }

    /// <summary>
    /// Deletes a resource in the specified uri and returns the response message
    /// </summary>
    public static async Task<TaskResult> DeleteAsync(string uri)
    {
        var response = await Http.DeleteAsync(uri);

        TaskResult result = new()
        {
            Message = await response.Content.ReadAsStringAsync(),
            Success = response.IsSuccessStatusCode
        };

        if (!result.Success)
        {
            Console.WriteLine("-----------------------------------------\n" +
                              "Failed PUT response for the following:\n" +
                              $"[{uri}]" +
                              $"Code: {response.StatusCode}" +
                              $"Message: {await response.Content.ReadAsStringAsync()}\n" +
                              $"-----------------------------------------");
        }

        return result;
    }
}