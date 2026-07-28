using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace Valour.Sdk.Utility;

/// <summary>
/// Portable P-256 proof key used by federation passports.
/// System.Security.Cryptography ECDsa is intentionally not used here because
/// key generation and verification are unavailable in browser WebAssembly and
/// have also failed on some Android runtimes.
/// </summary>
internal sealed class FederationProofKey
{
    private const int CoordinateSize = 32;

    private static readonly Org.BouncyCastle.Asn1.X9.X9ECParameters Curve =
        NistNamedCurves.GetByName("P-256")
        ?? throw new PlatformNotSupportedException("P-256 is unavailable.");

    private static readonly ECDomainParameters Domain = new(
        Curve.Curve,
        Curve.G,
        Curve.N,
        Curve.H,
        Curve.GetSeed());

    private readonly ECPrivateKeyParameters _privateKey;
    private readonly ECPublicKeyParameters _publicKey;

    private FederationProofKey(ECPrivateKeyParameters privateKey)
    {
        _privateKey = privateKey;
        _publicKey = new ECPublicKeyParameters(
            Domain.G.Multiply(privateKey.D).Normalize(),
            Domain);
    }

    public static FederationProofKey Generate()
    {
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(Domain, new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        return new FederationProofKey((ECPrivateKeyParameters)pair.Private);
    }

    public static FederationProofKey ImportPkcs8(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var key = PrivateKeyFactory.CreateKey(encoded) as ECPrivateKeyParameters
                  ?? throw new CryptographicException("The federation proof key is not an EC private key.");
        if (!key.Parameters.Curve.Equals(Domain.Curve) ||
            !key.Parameters.N.Equals(Domain.N) ||
            !key.Parameters.H.Equals(Domain.H) ||
            !key.Parameters.G.Normalize().Equals(Domain.G.Normalize()))
        {
            throw new CryptographicException("The federation proof key is not P-256.");
        }

        return new FederationProofKey(new ECPrivateKeyParameters(key.D, Domain));
    }

    public byte[] ExportPkcs8() => PrivateKeyInfoFactory.CreatePrivateKeyInfo(_privateKey).GetEncoded();

    public string ExportPublicJwk()
    {
        var point = _publicKey.Q.Normalize();
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64UrlEncode(ToFixedWidth(point.AffineXCoord.ToBigInteger())),
            ["y"] = Base64UrlEncode(ToFixedWidth(point.AffineYCoord.ToBigInteger())),
            ["alg"] = "ES256",
            ["use"] = "sig",
        });
    }

    public byte[] SignData(ReadOnlySpan<byte> data)
    {
        var signer = new ECDsaSigner(new HMacDsaKCalculator(new Sha256Digest()));
        signer.Init(true, _privateKey);
        var values = signer.GenerateSignature(SHA256.HashData(data));

        var signature = new byte[CoordinateSize * 2];
        ToFixedWidth(values[0]).CopyTo(signature, 0);
        ToFixedWidth(values[1]).CopyTo(signature, CoordinateSize);
        return signature;
    }

    public bool MatchesPublicJwk(string publicJwk)
    {
        if (!TryReadPublicJwk(publicJwk, out var publicKey))
            return false;

        return _publicKey.Q.Normalize().Equals(publicKey.Q.Normalize());
    }

    public static bool VerifyData(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, string publicJwk)
    {
        return TryReadPublicJwk(publicJwk, out var publicKey) &&
               VerifyData(data, signature, publicKey);
    }

    public static bool VerifyData(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> signature,
        ReadOnlySpan<byte> x,
        ReadOnlySpan<byte> y)
    {
        try
        {
            if (x.Length != CoordinateSize || y.Length != CoordinateSize)
                return false;

            var point = Domain.Curve.CreatePoint(
                new BigInteger(1, x.ToArray()),
                new BigInteger(1, y.ToArray())).Normalize();
            if (point.IsInfinity || !point.IsValid())
                return false;

            return VerifyData(data, signature, new ECPublicKeyParameters(point, Domain));
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyData(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> signature,
        ECPublicKeyParameters publicKey)
    {
        if (signature.Length != CoordinateSize * 2)
            return false;

        try
        {
            var r = new BigInteger(1, signature[..CoordinateSize].ToArray());
            var s = new BigInteger(1, signature[CoordinateSize..].ToArray());
            var verifier = new ECDsaSigner();
            verifier.Init(false, publicKey);
            return verifier.VerifySignature(SHA256.HashData(data), r, s);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadPublicJwk(string publicJwk, out ECPublicKeyParameters publicKey)
    {
        publicKey = null!;
        try
        {
            using var document = JsonDocument.Parse(publicJwk);
            var root = document.RootElement;
            if (!root.TryGetProperty("kty", out var kty) || kty.GetString() != "EC" ||
                !root.TryGetProperty("crv", out var curve) || curve.GetString() != "P-256" ||
                !root.TryGetProperty("x", out var xRaw) ||
                !root.TryGetProperty("y", out var yRaw))
            {
                return false;
            }

            var x = Base64UrlDecode(xRaw.GetString());
            var y = Base64UrlDecode(yRaw.GetString());
            if (x.Length != CoordinateSize || y.Length != CoordinateSize)
                return false;

            var point = Domain.Curve.CreatePoint(
                new BigInteger(1, x),
                new BigInteger(1, y)).Normalize();
            if (point.IsInfinity || !point.IsValid())
                return false;

            publicKey = new ECPublicKeyParameters(point, Domain);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] ToFixedWidth(BigInteger value)
    {
        var encoded = value.ToByteArrayUnsigned();
        if (encoded.Length > CoordinateSize)
            throw new CryptographicException("P-256 coordinate is too large.");

        if (encoded.Length == CoordinateSize)
            return encoded;

        var padded = new byte[CoordinateSize];
        encoded.CopyTo(padded, CoordinateSize - encoded.Length);
        return padded;
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static byte[] Base64UrlDecode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new FormatException("Missing base64url value.");

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
