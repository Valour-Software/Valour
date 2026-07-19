using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Valour.Sdk.Client;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Utilities;

namespace Valour.Sdk.Services;

/// <summary>
/// Handles tokens and authentication
/// </summary>
public class AuthService : ServiceBase
{
    /// <summary>
    /// Run when the user logs in
    /// </summary>
    public HybridEvent<User> LoggedIn;

    /// <summary>
    /// Run when the user is forcibly logged out (token expired or revoked)
    /// </summary>
    public HybridEvent<string> LoggedOut;
    
    /// <summary>
    /// The token for this client instance
    /// </summary>
    public string Token => _token;

    /// <summary>
    /// The internal token for this client
    /// </summary>
    private string _token;
    private ECDsa _federationPassportKey;
    private FederationPassportResponse _federationPassport;
    private string _federationHubJwks;
    private DateTime _federationHubJwksFetchedAt;

    private static readonly TimeSpan FederationJwksRefreshAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaximumOfflineFederationJwksAge = TimeSpan.FromMinutes(15);

    private static readonly LogOptions LogOptions = new(
        "AuthService",
        "#0083ab",
        "#ab0055",
        "#ab8900"
    );

    private readonly ValourClient _client;
    
    public AuthService(ValourClient client)
    {
        _client = client;
        SetupLogging(client.Logger, LogOptions);
    }
    
    /// <summary>
    /// Gets the Token for the client
    /// </summary>
    public async Task<AuthResult> FetchToken(string email, string password, string multiFactorCode = null)
    {
        TokenRequest request = new()
        {
            Email = email,
            Password = password,
            MultiFactorCode = multiFactorCode
        };

        var httpContent = JsonContent.Create(request);
        var response = await _client.Http.PostAsync($"api/users/token", httpContent);
        
        
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<AuthResult>();

            if (result.Token is not null){
                // A fresh login can represent a different account. Clear the
                // previous account's device-bound passport and proof key
                // before any federation operation can reuse them.
                SetToken(result.Token.Id);
            }

            return result;
        }

        return new AuthResult()
        {
            Success = false,
            Message = await response.Content.ReadAsStringAsync(),
            Code = (int) response.StatusCode
        };
    }
    
    public void SetToken(string token)
    {
        _token = token;
        ClearFederationPassportState();
        _client.NodeService?.InvalidateFederatedSessions("The hub account session changed.");
    }

    private void ClearFederationPassportState()
    {
        _federationPassport = null;
        _federationPassportKey?.Dispose();
        _federationPassportKey = null;
        _federationHubJwks = null;
        _federationHubJwksFetchedAt = default;
    }

    /// <summary>
    /// Gets a hub-signed identity passport tied to a device-local P-256 key.
    /// Hosts that need offline joins across app restarts should persist this
    /// key pair in their platform's secure storage and restore it before
    /// requesting a new passport.
    /// </summary>
    public async Task<TaskResult<FederationPassportResponse>> GetFederationPassportAsync()
    {
        if (_federationPassport?.Token is not null &&
            _federationPassport.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            return TaskResult<FederationPassportResponse>.FromData(_federationPassport);

        if (string.IsNullOrWhiteSpace(_token))
            return TaskResult<FederationPassportResponse>.FromFailure("Log in before requesting a federation passport.");

        _federationPassportKey ??= ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicParameters = _federationPassportKey.ExportParameters(false);
        var publicJwk = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64UrlEncode(publicParameters.Q.X),
            ["y"] = Base64UrlEncode(publicParameters.Q.Y),
            ["alg"] = "ES256",
            ["use"] = "sig",
        });

        try
        {
            using var response = await _client.Http.PostAsJsonAsync(
                "api/federation/passport",
                new FederationPassportRequest { PublicJwk = publicJwk });
            if (!response.IsSuccessStatusCode)
                return TaskResult<FederationPassportResponse>.FromFailure(await response.Content.ReadAsStringAsync());

            var passport = await response.Content.ReadFromJsonAsync<FederationPassportResponse>();
            if (string.IsNullOrWhiteSpace(passport?.Token) || passport.ExpiresAt <= DateTime.UtcNow)
                return TaskResult<FederationPassportResponse>.FromFailure("The hub returned an invalid federation passport.");

            _federationPassport = passport;
            // A short-lived public-key cache lets the client verify the signed
            // grant destination before sending a recipient proof while the hub
            // is temporarily unavailable. Failure here does not invalidate a
            // successful login/passport request; it only disables offline use.
            await RefreshFederationHubJwksAsync();
            return TaskResult<FederationPassportResponse>.FromData(passport);
        }
        catch (Exception e)
        {
            return TaskResult<FederationPassportResponse>.FromFailure(e.Message);
        }
    }

    /// <summary>
    /// Exports an unexpired passport and its client private key for a host to
    /// place in platform-secure storage. Do not put this object in ordinary
    /// preferences, logs, analytics, or a cloud backup.
    /// </summary>
    public FederationPassportCache ExportFederationPassportCache()
    {
        if (_federationPassport?.Token is null || _federationPassportKey is null ||
            _federationPassport.ExpiresAt <= DateTime.UtcNow)
            return null;

        return new FederationPassportCache
        {
            Passport = _federationPassport.Token,
            ExpiresAt = _federationPassport.ExpiresAt,
            PrivateKeyPkcs8 = Convert.ToBase64String(_federationPassportKey.ExportPkcs8PrivateKey()),
            HubJwks = _federationHubJwks,
            HubJwksFetchedAt = _federationHubJwksFetchedAt,
        };
    }

    /// <summary>
    /// Restores a passport cache after the user has logged in. The public key
    /// embedded in the signed passport must match the restored private key and
    /// the passport subject must be the current client user.
    /// </summary>
    public TaskResult ImportFederationPassportCache(FederationPassportCache cache)
    {
        if (cache is null || string.IsNullOrWhiteSpace(cache.Passport) ||
            string.IsNullOrWhiteSpace(cache.PrivateKeyPkcs8) || cache.ExpiresAt <= DateTime.UtcNow ||
            _client.Me is null)
            return TaskResult.FromFailure("The federation passport cache is invalid.");

        try
        {
            var subject = ReadJwtStringClaim(cache.Passport, "sub");
            var passportJwk = ReadJwtStringClaim(cache.Passport, "cnf");
            if (!long.TryParse(subject, out var userId) || userId != _client.Me.Id || string.IsNullOrWhiteSpace(passportJwk))
                return TaskResult.FromFailure("The federation passport belongs to a different account.");

            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(cache.PrivateKeyPkcs8), out _);
            if (!PublicJwkMatches(key, passportJwk))
            {
                key.Dispose();
                return TaskResult.FromFailure("The federation passport key does not match its proof key.");
            }

            _federationPassportKey?.Dispose();
            _federationPassportKey = key;
            _federationPassport = new FederationPassportResponse
            {
                Token = cache.Passport,
                ExpiresAt = cache.ExpiresAt,
            };
            _federationHubJwks = cache.HubJwks;
            _federationHubJwksFetchedAt = cache.HubJwksFetchedAt;
            return TaskResult.SuccessResult;
        }
        catch
        {
            return TaskResult.FromFailure("The federation passport cache is invalid.");
        }
    }

    /// <summary>
    /// Builds the recipient proof for one specific grant scope. A node cannot
    /// use this signature to redeem a different invite or redirect it to a
    /// different destination, even though it has seen the passport JWT.
    /// </summary>
    public async Task<TaskResult<FederatedInviteRedeemRequest>> CreateFederatedInviteRedeemRequestAsync(string grant)
    {
        if (string.IsNullOrWhiteSpace(grant))
            return TaskResult<FederatedInviteRedeemRequest>.FromFailure("Include a federation invite grant.");

        var passport = await GetFederationPassportAsync();
        if (!passport.Success)
            return TaskResult<FederatedInviteRedeemRequest>.FromFailure(passport.Message);

        var grantId = ReadJwtStringClaim(grant, "jti");
        var nodeDomain = ReadJwtStringClaim(grant, "node_domain");
        var planetIdRaw = ReadJwtStringClaim(grant, "planet_id");
        var recipientIdRaw = ReadJwtStringClaim(grant, "recipient_id");
        var maxUsesRaw = ReadJwtStringClaim(grant, "max_uses");
        var passportId = ReadJwtStringClaim(passport.Data.Token, "jti");
        var subject = ReadJwtStringClaim(passport.Data.Token, "sub");
        if (string.IsNullOrWhiteSpace(grantId) || string.IsNullOrWhiteSpace(passportId) ||
            string.IsNullOrWhiteSpace(nodeDomain) ||
            !long.TryParse(planetIdRaw, out var planetId) || planetId <= 0 ||
            !long.TryParse(recipientIdRaw, out var recipientId) || recipientId <= 0 ||
            !int.TryParse(maxUsesRaw, out var maxUses) || maxUses != 1 ||
            !long.TryParse(subject, out var userId) || _federationPassportKey is null)
            return TaskResult<FederatedInviteRedeemRequest>.FromFailure("The federation invite or passport is malformed.");

        var proofPayload = ValourFederation.BuildInviteProofPayload(
            grantId, nodeDomain, planetId, recipientId, maxUses, passportId, userId);
        var signature = _federationPassportKey.SignData(Encoding.UTF8.GetBytes(proofPayload), HashAlgorithmName.SHA256);
        return TaskResult<FederatedInviteRedeemRequest>.FromData(new FederatedInviteRedeemRequest
        {
            Grant = grant,
            Passport = passport.Data.Token,
            Proof = Base64UrlEncode(signature),
        });
    }

    /// <summary>
    /// Returns the destination from a valid, hub-signed invite grant. This is
    /// intentionally performed before building or transmitting the passport
    /// proof, so a tampered JWT cannot use an unverified node_domain claim as
    /// an exfiltration target. A cached JWKS remains usable only for the same
    /// bounded outage window accepted by community nodes.
    /// </summary>
    public async Task<TaskResult<string>> GetFederatedInviteDestinationAsync(string grant)
    {
        if (string.IsNullOrWhiteSpace(grant))
            return TaskResult<string>.FromFailure("Include a federation invite grant.");

        await EnsureFederationHubJwksAsync();
        if (!TryValidateFederatedInviteGrant(grant, out var destination))
            return TaskResult<string>.FromFailure("The federation invite could not be verified for a safe destination.");

        return TaskResult<string>.FromData(destination);
    }

    private async Task EnsureFederationHubJwksAsync()
    {
        if (!string.IsNullOrWhiteSpace(_federationHubJwks) &&
            DateTime.UtcNow - _federationHubJwksFetchedAt <= FederationJwksRefreshAge)
        {
            return;
        }

        await RefreshFederationHubJwksAsync();
    }

    private async Task RefreshFederationHubJwksAsync()
    {
        try
        {
            using var response = await _client.Http.GetAsync(ValourFederation.HubWellKnownRoute);
            if (!response.IsSuccessStatusCode)
                return;

            var jwks = await response.Content.ReadAsStringAsync();
            if (!ContainsUsableFederationJwk(jwks))
                return;

            _federationHubJwks = jwks;
            _federationHubJwksFetchedAt = DateTime.UtcNow;
        }
        catch
        {
            // If the hub is unavailable, TryValidateFederatedInviteGrant will
            // only permit a still-fresh cached key set below.
        }
    }

    private bool TryValidateFederatedInviteGrant(string grant, out string destination)
    {
        destination = null;
        if (string.IsNullOrWhiteSpace(_federationHubJwks) ||
            _federationHubJwksFetchedAt == default ||
            DateTime.UtcNow - _federationHubJwksFetchedAt > MaximumOfflineFederationJwksAge)
        {
            return false;
        }

        try
        {
            var parts = grant.Split('.');
            if (parts.Length != 3)
                return false;

            using var header = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
            using var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
            if (!header.RootElement.TryGetProperty("alg", out var algorithm) ||
                !string.Equals(algorithm.GetString(), "ES256", StringComparison.Ordinal) ||
                !header.RootElement.TryGetProperty("kid", out var keyId) ||
                string.IsNullOrWhiteSpace(keyId.GetString()) ||
                !TryGetFederationSigningKey(_federationHubJwks, keyId.GetString(), out var parameters) ||
                !VerifyEs256Signature(parts[0] + "." + parts[1], parts[2], parameters))
            {
                return false;
            }

            var claims = payload.RootElement;
            if (!HasStringClaim(claims, "purpose", ValourFederation.InvitePurpose) ||
                !HasIntegerClaim(claims, "protocol", ValourFederation.ProtocolVersion) ||
                !HasCurrentLifetime(claims) ||
                !TryGetStringClaim(claims, "aud", out var audience) ||
                !TryGetStringClaim(claims, "node_domain", out var nodeDomain))
            {
                return false;
            }

            var normalizedAudience = NodeService.NormalizeFederationDomain(audience);
            var normalizedNodeDomain = NodeService.NormalizeFederationDomain(nodeDomain);
            if (normalizedAudience is null || normalizedNodeDomain is null ||
                !string.Equals(normalizedAudience, normalizedNodeDomain, StringComparison.Ordinal))
            {
                return false;
            }

            destination = normalizedNodeDomain;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsUsableFederationJwk(string jwks)
    {
        try
        {
            using var document = JsonDocument.Parse(jwks);
            if (!document.RootElement.TryGetProperty("keys", out var keys) ||
                keys.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var key in keys.EnumerateArray())
            {
                if (TryGetFederationSigningKey(key, out _))
                    return true;
            }
        }
        catch
        {
            // malformed JWKS is not cacheable
        }

        return false;
    }

    private static bool TryGetFederationSigningKey(string jwks, string keyId, out ECParameters parameters)
    {
        parameters = default;
        try
        {
            using var document = JsonDocument.Parse(jwks);
            if (!document.RootElement.TryGetProperty("keys", out var keys) ||
                keys.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var key in keys.EnumerateArray())
            {
                if (key.TryGetProperty("kid", out var candidateId) &&
                    string.Equals(candidateId.GetString(), keyId, StringComparison.Ordinal) &&
                    TryGetFederationSigningKey(key, out parameters))
                {
                    return true;
                }
            }
        }
        catch
        {
            // malformed JWKS is not usable
        }

        return false;
    }

    private static bool TryGetFederationSigningKey(JsonElement key, out ECParameters parameters)
    {
        parameters = default;
        if (!HasStringClaim(key, "kty", "EC") || !HasStringClaim(key, "crv", "P-256") ||
            !HasStringClaim(key, "alg", "ES256") || !HasStringClaim(key, "use", "sig") ||
            !TryGetStringClaim(key, "x", out var x) || !TryGetStringClaim(key, "y", out var y))
        {
            return false;
        }

        try
        {
            var qx = Base64UrlDecode(x);
            var qy = Base64UrlDecode(y);
            if (qx.Length != 32 || qy.Length != 32)
                return false;

            parameters = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = qx, Y = qy },
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyEs256Signature(string signingInput, string signature, ECParameters parameters)
    {
        try
        {
            var signatureBytes = Base64UrlDecode(signature);
            if (signatureBytes.Length != 64)
                return false;

            using var key = ECDsa.Create(parameters);
            return key.VerifyData(
                Encoding.ASCII.GetBytes(signingInput),
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasCurrentLifetime(JsonElement claims)
    {
        var now = DateTimeOffset.UtcNow;
        if (!TryGetUnixTimeClaim(claims, "exp", out var expiresAt) || expiresAt < now.AddMinutes(-1))
            return false;

        return !TryGetUnixTimeClaim(claims, "nbf", out var notBefore) || notBefore <= now.AddMinutes(1);
    }

    private static bool TryGetUnixTimeClaim(JsonElement claims, string name, out DateTimeOffset value)
    {
        value = default;
        return claims.TryGetProperty(name, out var raw) && raw.ValueKind == JsonValueKind.Number &&
               raw.TryGetInt64(out var seconds) && TryFromUnixTimeSeconds(seconds, out value);
    }

    private static bool TryFromUnixTimeSeconds(long seconds, out DateTimeOffset value)
    {
        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    private static bool HasStringClaim(JsonElement claims, string name, string expected) =>
        TryGetStringClaim(claims, name, out var actual) && string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool HasIntegerClaim(JsonElement claims, string name, int expected) =>
        claims.TryGetProperty(name, out var raw) && raw.ValueKind == JsonValueKind.Number &&
        raw.TryGetInt32(out var actual) && actual == expected;

    private static bool TryGetStringClaim(JsonElement claims, string name, out string value)
    {
        value = null;
        return claims.TryGetProperty(name, out var raw) && raw.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = raw.GetString());
    }

    private static string ReadJwtStringClaim(string token, string claim)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
            if (!document.RootElement.TryGetProperty(claim, out var value))
                return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }

    private static bool PublicJwkMatches(ECDsa key, string passportJwk)
    {
        try
        {
            using var document = JsonDocument.Parse(passportJwk);
            var root = document.RootElement;
            var parameters = key.ExportParameters(false);
            return root.TryGetProperty("kty", out var kty) && kty.GetString() == "EC" &&
                   root.TryGetProperty("crv", out var crv) && crv.GetString() == "P-256" &&
                   root.TryGetProperty("x", out var x) && x.GetString() == Base64UrlEncode(parameters.Q.X) &&
                   root.TryGetProperty("y", out var y) && y.GetString() == Base64UrlEncode(parameters.Q.Y);
        }
        catch
        {
            return false;
        }
    }

    public async Task<AuthResult> LoginAsync(string email, string password, string multiFactorCode = null)
    {
        var tokenResult = await FetchToken(email, password, multiFactorCode);
        if (!tokenResult.Success)
        {
            return tokenResult;
        }

        var loginResult = await LoginAsync();
        if (!loginResult.Success)
        {
            return new AuthResult()
            {
                Success = false,
                Message = loginResult.Message,
            };
        }

        return new AuthResult()
        {
            Success = true,
            Message = "Success"
        };
    }
    
    public async Task<TaskResult> LoginAsync()
    {
        // Ensure any existing auth headers are removed
        if (_client.Http.DefaultRequestHeaders.Contains("authorization"))
        {
            _client.Http.DefaultRequestHeaders.Remove("authorization");
        }
        
        // Add auth header to main http client so we never have to do that again
        _client.Http.DefaultRequestHeaders.Add("authorization", Token);

        if (_client.PrimaryNode is null)
        {
            var nodeResult = await _client.NodeService.SetupPrimaryNodeAsync();
            if (!nodeResult.Success)
                return nodeResult;
        }
        else
        {
            // Update the token if it's already been set
            _client.PrimaryNode.UpdateToken();
        }

        var response = await _client.PrimaryNode!.GetJsonAsync<User>($"api/users/me");

        if (!response.Success)
            return response.WithoutData();

        _client.Me = response.Data.Sync(_client);

        // Best-effort prefetch while the hub is available. This leaves a
        // recently logged-in client ready to redeem a recipient-bound invite
        // if the hub goes down before it reaches the community node.
        _ = GetFederationPassportAsync();
        
        LoggedIn?.Invoke(_client.Me);

        return new TaskResult(true, "Success");
    }

    public async Task<TaskResult> RegisterAsync(RegisterUserRequest request)
    {
        var content = JsonContent.Create(request);
        var result = await _client.Http.PostAsync("api/users/register", content);

        if (result.IsSuccessStatusCode)
        {
            return TaskResult.SuccessResult;
        }
        
        var text = "";

        try
        {
            text = await result.Content.ReadAsStringAsync();
        } catch (Exception ex)
        {
            text = "Unknown error";
        }
        
        return TaskResult.FromFailure(text, (int)result.StatusCode);
    }
    
    /// <summary>
    /// Sets the compliance data for the current user
    /// </summary>
    public async ValueTask<TaskResult> SetComplianceDataAsync(DateTime birthDate)
    {
        var result = await _client.PrimaryNode.PostAsync($"api/users/me/compliance/{birthDate.ToString("s")}", null);
        var taskResult = new TaskResult()
        {
            Success = result.Success,
            Message = result.Message
        };

        return taskResult;
    }
    
    /// <summary>
    /// Returns all the multi-factor authentication methods available to the user
    /// </summary>
    public async Task<List<string>> GetMfaMethodsAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<string>>("api/users/me/multiauth");

        if (!response.Success)
        {
            LogError($"Failed to get multi-auth methods: {response.Message}");
            return [];
        }
        
        return response.Data;
    }
    
    
    /// <summary>
    /// Requests and sets up a multi-factor authentication key
    /// </summary>
    public async Task<TaskResult<CreateAppMultiAuthResponse>> SetupMfaAsync()
    {
        return await _client.PrimaryNode.PostAsyncWithResponse<CreateAppMultiAuthResponse>($"api/users/me/multiAuth", null);
    }
    
    public async Task<TaskResult> VerifyMfaAsync(string code)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<bool>($"api/users/me/multiAuth/verify/{code}", null);
        
        if (!result.Success)
            return new TaskResult(false, result.Message);
        
        if (!result.Data)
            return new TaskResult(false, "Invalid code");

        return TaskResult.SuccessResult;
    }
    
    public async Task<TaskResult> RemoveMfaAsync(string password)
    {
        var request = new RemoveMfaRequest()
        {
            Password = password
        };
        
        return await _client.PrimaryNode.PostAsync("api/users/me/multiAuth/remove", request);
    }

    internal void HandleTokenInvalidated(string reason = null)
    {
        if (_token is null)
            return;

        SetToken(null);

        if (_client.Http.DefaultRequestHeaders.Contains("authorization"))
            _client.Http.DefaultRequestHeaders.Remove("authorization");

        LoggedOut?.Invoke(reason);
    }

    #region Token Management

    /// <summary>
    /// Gets all tokens for the current user
    /// </summary>
    public async Task<List<AuthToken>> GetMyTokensAsync()
    {
        var response = await _client.PrimaryNode.GetJsonAsync<List<AuthToken>>("api/users/me/tokens");
        return response.Success ? response.Data : new List<AuthToken>();
    }

    /// <summary>
    /// Revokes a specific token
    /// </summary>
    public async Task<TaskResult> RevokeTokenAsync(string tokenId)
    {
        return await _client.PrimaryNode.DeleteAsync($"api/users/me/tokens/{tokenId}");
    }

    /// <summary>
    /// Revokes all other tokens (except the current one)
    /// </summary>
    public async Task<TaskResult> RevokeAllOtherTokensAsync()
    {
        return await _client.PrimaryNode.DeleteAsync("api/users/me/tokens");
    }

    /// <summary>
    /// Logs out the current user (revokes current token) and clears local auth state.
    /// The server call is best-effort: local state is cleared even if it fails so the
    /// device never keeps using a token the user asked to revoke.
    /// </summary>
    public async Task<TaskResult> LogoutAsync()
    {
        TaskResult result;
        try
        {
            result = await _client.PrimaryNode.PostAsync("api/users/me/logout", null);
        }
        catch (Exception ex)
        {
            result = new TaskResult(false, ex.Message);
        }

        SetToken(null);

        try
        {
            if (_client.Http.DefaultRequestHeaders.Contains("authorization"))
                _client.Http.DefaultRequestHeaders.Remove("authorization");
        }
        catch
        {
            // Best-effort header cleanup
        }

        return result;
    }

    #endregion
}
