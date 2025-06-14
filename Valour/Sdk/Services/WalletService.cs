using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Valour.Sdk.Client;
using Valour.Shared;

namespace Valour.Sdk.Services;

public class WalletService : ServiceBase
{
    private readonly ValourClient _client;
    private readonly LogOptions _logOptions = new(
        "WalletService",
        "#3381a3",
        "#a3333e",
        "#a39433"
    );

    public WalletService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, _logOptions);
    }

    public async Task<string?> SyncWithWallet()
    {
        var token = _client.AuthService.Token;
        var request = CreateRequest(HttpMethod.Post, "api/userWallet/nonce", token);
        var response = await _client.Http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var data = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<string>(data);
        }

        Console.WriteLine("Nonce not found in response.");
        return null;
    }

    public async Task<TaskResult> SignNonce(string publicKey, string nonce, string signature
        , string vlrc,string provider)
    {

        try
        {
            if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature))
            {
                return new TaskResult
                {
                    Success = false,
                    Message = "Fail sign nonce",
                    Details = "The nonce or signature is empty"
                };
            }

            var payload = new
            {
                PublicKey = publicKey,
                Nonce = nonce,
                Signature = signature,
                Vlrc = vlrc,
                Provider = provider
            };

            var token = _client.AuthService.Token;
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = CreateRequest(HttpMethod.Post, "api/userWallet/signature", token);
            request.Content = content;

            var response = await _client.Http.SendAsync(request);
            return await response.Content.ReadFromJsonAsync<TaskResult>();
        } 
        catch (Exception e){
            return TaskResult.FromFailure("An error occured",400,"An error occured verifying the nonce and public Key");
        }
    }

    public async Task<TaskResult> DisconnectWallet(string publicKey)
    {
        var token = _client.AuthService.Token;
        var url = $"api/userWallet/disconnect?publicKey={Uri.EscapeDataString(publicKey)}";
        var request = CreateRequest(HttpMethod.Post, url, token);
        var response = await _client.Http.SendAsync(request);
        return await response.Content.ReadFromJsonAsync<TaskResult>();
    }

    public async Task<TaskResult> CheckWalletConnection(string publicKey)
    {
        var token = _client.AuthService.Token;
        var url = $"api/userWallet/isConnected?publicKey={Uri.EscapeDataString(publicKey)}";
        var request = CreateRequest(HttpMethod.Get, url, token);
        var response = await _client.Http.SendAsync(request);
        var data = await response.Content.ReadAsStringAsync(); 
        return JsonSerializer.Deserialize<TaskResult>(data);
    }

    public async Task<long> VlrcBalance(string publicKey)
    {
        var token = _client.AuthService.Token;
        var url = $"api/userWallet/vlrcBalance?publicKey={Uri.EscapeDataString(publicKey)}";
        var request = CreateRequest(HttpMethod.Get, url, token);
        var response = await _client.Http.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var data = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<long>(data);
        }
        Console.WriteLine("vlrc not found in response.");
        return 0;
    }
    
    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string? token = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(token);
        }
        return request;
    }
}
