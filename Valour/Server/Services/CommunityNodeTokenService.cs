using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Valour.Config.Configs;
using Valour.Shared.Authorization;
using Valour.Shared.Nodes;
using UserModel = Valour.Server.Models.User;

namespace Valour.Server.Services;

public class CommunityUserSnapshot
{
    public string Name { get; set; }
    public string Tag { get; set; }
    public DateTime TimeJoined { get; set; }
    public DateTime TimeLastActive { get; set; }
    public bool HasCustomAvatar { get; set; }
    public bool HasAnimatedAvatar { get; set; }
    public bool Bot { get; set; }
    public bool Disabled { get; set; }
    public bool ValourStaff { get; set; }
    public string Status { get; set; }
    public int UserStateCode { get; set; }
    public bool IsMobile { get; set; }
    public bool Compliance { get; set; }
    public string SubscriptionType { get; set; }
    public string PriorName { get; set; }
    public DateTime? NameChangeTime { get; set; }
    public int Version { get; set; }
    public long TutorialState { get; set; }
    public long? OwnerId { get; set; }
    public string StarColor1 { get; set; }
    public string StarColor2 { get; set; }
}

public class CommunityTokenPayload
{
    public string TokenType { get; set; } = CommunityTokenType;
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public string NodeId { get; set; }
    public long UserId { get; set; }
    public long Scope { get; set; }
    public long IssuedAtUnix { get; set; }
    public long ExpiresAtUnix { get; set; }
    public CommunityUserSnapshot User { get; set; }

    public const string CommunityTokenType = "community";
}

public class CommunityNodeTokenService
{
    public const string TokenPrefix = "vctc1";
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CommunityShadowUserService _shadowUserService;
    private readonly ILogger<CommunityNodeTokenService> _logger;

    public CommunityNodeTokenService(
        CommunityShadowUserService shadowUserService,
        ILogger<CommunityNodeTokenService> logger)
    {
        _shadowUserService = shadowUserService;
        _logger = logger;
    }

    public static bool IsCommunityToken(string token) =>
        !string.IsNullOrWhiteSpace(token) &&
        token.StartsWith(TokenPrefix + ".", StringComparison.Ordinal);

    public Task<CommunityNodeTokenExchangeResponse> IssueAsync(UserModel user, string nodeId, string canonicalOrigin)
    {
        if (NodeConfig.Instance.Mode != NodeMode.Official)
            throw new InvalidOperationException("Only official nodes can mint community tokens.");

        var normalizedOrigin = NormalizeOrigin(canonicalOrigin);
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.Add(TokenLifetime);

        var payload = new CommunityTokenPayload
        {
            Issuer = NormalizeOrigin(NodeConfig.Instance.CanonicalOrigin),
            Audience = normalizedOrigin,
            NodeId = nodeId?.Trim(),
            UserId = user.Id,
            Scope = UserPermissions.CommunityDefault.Value,
            IssuedAtUnix = new DateTimeOffset(issuedAt).ToUnixTimeSeconds(),
            ExpiresAtUnix = new DateTimeOffset(expiresAt).ToUnixTimeSeconds(),
            User = CreateSnapshot(user)
        };

        var token = SignPayload(payload);

        return Task.FromResult(new CommunityNodeTokenExchangeResponse
        {
            Token = token,
            TimeExpires = expiresAt
        });
    }

    public async Task<AuthToken> ValidateAsync(string token)
    {
        if (NodeConfig.Instance.Mode != NodeMode.Community)
            return null;

        if (!TryReadPayload(token, out var payload))
            return null;

        if (!string.Equals(payload.TokenType, CommunityTokenPayload.CommunityTokenType, StringComparison.Ordinal))
            return null;

        if (!string.Equals(payload.NodeId, ResolveNodeId(), StringComparison.Ordinal))
            return null;

        if (!string.Equals(payload.Audience, NormalizeOrigin(NodeConfig.Instance.CanonicalOrigin), StringComparison.Ordinal))
            return null;

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAtUnix).UtcDateTime;
        if (expiresAt <= DateTime.UtcNow)
            return null;

        await _shadowUserService.EnsureShadowUserAsync(payload);

        return new AuthToken
        {
            Id = token,
            TokenType = CommunityTokenPayload.CommunityTokenType,
            AppId = "COMMUNITY_NODE",
            UserId = payload.UserId,
            Scope = payload.Scope,
            TimeCreated = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtUnix).UtcDateTime,
            TimeExpires = expiresAt,
            Audience = payload.Audience,
            IssuedAddress = payload.Issuer
        };
    }

    public static string NormalizeOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return string.Empty;

        var uri = new Uri(origin.Trim(), UriKind.Absolute);
        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    public static string ResolveNodeId()
    {
        if (!string.IsNullOrWhiteSpace(NodeConfig.Instance.Id))
            return NodeConfig.Instance.Id.Trim();

        return !string.IsNullOrWhiteSpace(NodeConfig.Instance.Name)
            ? NodeConfig.Instance.Name.Trim()
            : NormalizeOrigin(NodeConfig.Instance.CanonicalOrigin);
    }

    private string SignPayload(CommunityTokenPayload payload)
    {
        using var rsa = CreatePrivateKey();
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var encodedPayload = Base64UrlEncode(payloadBytes);
        var signatureBytes = rsa.SignData(Encoding.UTF8.GetBytes(encodedPayload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{TokenPrefix}.{encodedPayload}.{Base64UrlEncode(signatureBytes)}";
    }

    private bool TryReadPayload(string token, out CommunityTokenPayload payload)
    {
        payload = null;

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3 || !string.Equals(parts[0], TokenPrefix, StringComparison.Ordinal))
                return false;

            using var rsa = CreatePublicKey();
            var payloadSegment = parts[1];
            var signatureBytes = Base64UrlDecode(parts[2]);
            var isValid = rsa.VerifyData(
                Encoding.UTF8.GetBytes(payloadSegment),
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            if (!isValid)
                return false;

            var payloadBytes = Base64UrlDecode(payloadSegment);
            payload = JsonSerializer.Deserialize<CommunityTokenPayload>(payloadBytes, JsonOptions);
            return payload is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate community token.");
            return false;
        }
    }

    private static CommunityUserSnapshot CreateSnapshot(UserModel user)
    {
        return new CommunityUserSnapshot
        {
            Name = user.Name,
            Tag = user.Tag,
            TimeJoined = user.TimeJoined,
            TimeLastActive = user.TimeLastActive,
            HasCustomAvatar = user.HasCustomAvatar,
            HasAnimatedAvatar = user.HasAnimatedAvatar,
            Bot = user.Bot,
            Disabled = user.Disabled,
            ValourStaff = user.ValourStaff,
            Status = user.Status,
            UserStateCode = user.UserStateCode,
            IsMobile = user.IsMobile,
            Compliance = user.Compliance,
            SubscriptionType = user.SubscriptionType,
            PriorName = user.PriorName,
            NameChangeTime = user.NameChangeTime,
            Version = user.Version,
            TutorialState = user.TutorialState,
            OwnerId = user.OwnerId,
            StarColor1 = user.StarColor1,
            StarColor2 = user.StarColor2
        };
    }

    private static RSA CreatePrivateKey()
    {
        var path = NodeConfig.Instance.CommunityTokenPrivateKeyPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Node.CommunityTokenPrivateKeyPath must be configured on official nodes.");

        var pem = File.ReadAllText(path);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    private static RSA CreatePublicKey()
    {
        var path = NodeConfig.Instance.CommunityTokenPublicKeyPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Node.CommunityTokenPublicKeyPath must be configured on community nodes.");

        var pem = File.ReadAllText(path);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value
            .Replace('-', '+')
            .Replace('_', '/');

        var padding = 4 - (base64.Length % 4);
        if (padding is > 0 and < 4)
            base64 = base64.PadRight(base64.Length + padding, '=');

        return Convert.FromBase64String(base64);
    }
}
