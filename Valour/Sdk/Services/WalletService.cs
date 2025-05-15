using System.Net;
using System.Net.Http.Headers;
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

    public async Task<string?> SyncWhitWallet(string publicKey)
    {
            var token = _client.AuthService.Token;
            var request = new HttpRequestMessage(HttpMethod.Get, $"api/userWallet/nonce?publicKey={Uri.EscapeDataString(publicKey)}");
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

    
    public async Task<HttpStatusCode> SignNonce(string publicKey, string nonce, string signature)
    {
        if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature))
            return HttpStatusCode.NoContent;

        var payload = new
        {
            PublicKey = publicKey,
            Nonce = nonce,
            Signature = signature
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
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> VerifyWallet(string publicKey)
    {
        var token = _client.AuthService.Token;
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/userWallet/verify?publicKey={Uri.EscapeDataString(publicKey)}");
        request.Headers.Authorization = new AuthenticationHeaderValue(token);
        var response = await _client.Http.SendAsync(request);
        return response.StatusCode;
    }
    
    public async Task<HttpStatusCode> DisconnectWallet(string publicKey)
    {
        var token = _client.AuthService.Token;
        var request = new HttpRequestMessage(HttpMethod.Get, $"api/userWallet/disconnect?publicKey={Uri.EscapeDataString(publicKey)}");
        request.Headers.Authorization = new AuthenticationHeaderValue(token);
        var response = await _client.Http.SendAsync(request);
        return response.StatusCode;
    }
    
}
