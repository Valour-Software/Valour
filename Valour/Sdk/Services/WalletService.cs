using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Valour.Sdk.Client;


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
            var request = new HttpRequestMessage(HttpMethod.Post, $"api/userWallet/nonce");
            request.Headers.Authorization = new AuthenticationHeaderValue(token);
            var response = await _client.Http.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadAsStringAsync();
                var nonce = JsonSerializer.Deserialize<string>(data);
                return nonce;
            }
            Console.WriteLine("Nonce not found in response.");
            return null;
    }

    
    public async Task<bool> SignNonce(string publicKey, string nonce, string signature,string vlrc)
    {
        if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature))
            return false;

        var payload = new
        {
            PublicKey = publicKey,
            Nonce = nonce,
            Signature = signature,
            Vlrc = vlrc
        };

        var token = _client.AuthService.Token;
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var request = new HttpRequestMessage(HttpMethod.Post, "api/userWallet/signature")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(token);
        var response = await _client.Http.SendAsync(request);
        var data = await response.Content.ReadFromJsonAsync<bool>();
        return data;
    }

    public async Task<bool> UserHasWallet(string publicKey)
    {
        var token = _client.AuthService.Token;
        var request = new HttpRequestMessage(HttpMethod.Post, $"api/userWallet/verifyWallet?publicKey={Uri.EscapeDataString(publicKey)}");
        request.Headers.Authorization = new AuthenticationHeaderValue(token);
        var response = await _client.Http.SendAsync(request);
        var data = await response.Content.ReadFromJsonAsync<bool>();
        return data;
    }
    
    public async Task<HttpContent> DisconnectWallet(string publicKey)
    {
        var token = _client.AuthService.Token;
        var request = new HttpRequestMessage(HttpMethod.Post, $"api/userWallet/disconnect?publicKey={Uri.EscapeDataString(publicKey)}");
        request.Headers.Authorization = new AuthenticationHeaderValue(token);
        var response = await _client.Http.SendAsync(request);
        return response.Content;
    }

    public async Task<bool> CheckWalletConnection(string publicKey)
    {
        var token = _client.AuthService.Token;
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/userWallet/isConnected?publicKey={Uri.EscapeDataString(publicKey)}");
        request.Headers.Authorization = new AuthenticationHeaderValue(token);
        var response = await _client.Http.SendAsync(request);
        var data = await response.Content.ReadFromJsonAsync<bool>();
        return data ;
    }

    public async Task<long> VlrcBalance(string publicKey)
    {
        var token = _client.AuthService.Token;
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/userWallet/vlrcBalance?publicKey={Uri.EscapeDataString(publicKey)}");
        request.Headers.Authorization = new AuthenticationHeaderValue(token);
        var response = await _client.Http.SendAsync(request);
            
        if (response.IsSuccessStatusCode)
        {
            var data = await response.Content.ReadAsStringAsync();
            var vlrc = JsonSerializer.Deserialize<long>(data);
            return vlrc;
        }
        Console.WriteLine("vlrc not found in response.");
        return 0;
    }
    
    
}
