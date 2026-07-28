using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Valour.Sdk.Utility;

namespace Valour.Tests.Services;

public class FederationProofKeyTests
{
    [Fact]
    public void PortableSignature_IsAcceptedBySystemEcdsa()
    {
        var data = Encoding.UTF8.GetBytes("federation-proof");
        var key = FederationProofKey.Generate();
        var signature = key.SignData(data);

        using var systemKey = ECDsa.Create();
        using var jwk = JsonDocument.Parse(key.ExportPublicJwk());
        systemKey.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64UrlDecode(jwk.RootElement.GetProperty("x").GetString()!),
                Y = Base64UrlDecode(jwk.RootElement.GetProperty("y").GetString()!),
            }
        });

        Assert.True(systemKey.VerifyData(
            data,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void SystemSignature_IsAcceptedByPortableVerifier()
    {
        var data = Encoding.UTF8.GetBytes("hub-signed-grant");
        using var systemKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = systemKey.ExportParameters(false);
        var publicJwk = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url(parameters.Q.X!),
            ["y"] = Base64Url(parameters.Q.Y!),
            ["alg"] = "ES256",
            ["use"] = "sig",
        });
        var signature = systemKey.SignData(
            data,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        Assert.True(FederationProofKey.VerifyData(data, signature, publicJwk));
    }

    [Fact]
    public void ExportedProofKey_RoundTripsAndRejectsAnotherPublicKey()
    {
        var original = FederationProofKey.Generate();
        var restored = FederationProofKey.ImportPkcs8(original.ExportPkcs8());
        var other = FederationProofKey.Generate();

        Assert.True(restored.MatchesPublicJwk(original.ExportPublicJwk()));
        Assert.False(restored.MatchesPublicJwk(other.ExportPublicJwk()));

        var data = Encoding.UTF8.GetBytes("round-trip");
        var signature = restored.SignData(data);
        Assert.True(FederationProofKey.VerifyData(
            data,
            signature,
            original.ExportPublicJwk()));
    }

    [Fact]
    public void ExistingSystemPkcs8Key_ImportsIntoPortableProvider()
    {
        using var systemKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var imported = FederationProofKey.ImportPkcs8(systemKey.ExportPkcs8PrivateKey());
        var data = Encoding.UTF8.GetBytes("existing-cache");
        var signature = imported.SignData(data);
        var parameters = systemKey.ExportParameters(false);

        Assert.True(FederationProofKey.VerifyData(
            data,
            signature,
            parameters.Q.X!,
            parameters.Q.Y!));
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
