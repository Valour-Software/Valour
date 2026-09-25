using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Valour.Sdk.E2ee;

/// <summary>
/// Cryptographic primitives for end-to-end encryption.
///
/// BouncyCastle is used instead of System.Security.Cryptography because the
/// browser WebAssembly runtime does not implement X25519, Ed25519, or an AEAD
/// cipher, and the SDK must produce identical results in browsers, native apps,
/// bots, and the server.
///
/// Primitives: X25519 key agreement, Ed25519 signatures, HKDF-SHA256,
/// HMAC-SHA256, SHA-256, and ChaCha20-Poly1305. ChaCha20 is used rather than
/// AES because it is fast and constant-time without hardware support, which
/// matters for interpreted WebAssembly.
/// </summary>
public static class E2eeCrypto
{
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int SignatureSize = 64;
    public const int HashSize = 32;

    public static byte[] RandomBytes(int length) => RandomNumberGenerator.GetBytes(length);

    // X25519

    public static byte[] X25519PublicKey(byte[] privateKey)
    {
        RequireLength(privateKey, KeySize, "X25519 private key");
        return new X25519PrivateKeyParameters(privateKey, 0).GeneratePublicKey().GetEncoded();
    }

    public static (byte[] PrivateKey, byte[] PublicKey) GenerateX25519()
    {
        var privateKey = RandomBytes(KeySize);
        return (privateKey, X25519PublicKey(privateKey));
    }

    public static byte[] X25519Agree(byte[] privateKey, byte[] publicKey)
    {
        RequireLength(privateKey, KeySize, "X25519 private key");
        RequireLength(publicKey, KeySize, "X25519 public key");

        var agreement = new X25519Agreement();
        agreement.Init(new X25519PrivateKeyParameters(privateKey, 0));
        var secret = new byte[agreement.AgreementSize];

        // A low-order public key produces an all-zero secret, which BouncyCastle
        // rejects. Refusing it prevents a sender from forcing a predictable key.
        try
        {
            agreement.CalculateAgreement(new X25519PublicKeyParameters(publicKey, 0), secret, 0);
        }
        catch (InvalidOperationException)
        {
            throw new E2eeVerificationException("Invalid X25519 public key.");
        }

        if (secret.All(b => b == 0))
            throw new E2eeVerificationException("Invalid X25519 public key.");

        return secret;
    }

    // Ed25519

    public static byte[] Ed25519PublicKey(byte[] seed)
    {
        RequireLength(seed, KeySize, "Ed25519 seed");
        return new Ed25519PrivateKeyParameters(seed, 0).GeneratePublicKey().GetEncoded();
    }

    public static (byte[] Seed, byte[] PublicKey) GenerateEd25519()
    {
        var seed = RandomBytes(KeySize);
        return (seed, Ed25519PublicKey(seed));
    }

    public static byte[] Sign(byte[] seed, byte[] message)
    {
        RequireLength(seed, KeySize, "Ed25519 seed");
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(seed, 0));
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        if (publicKey is null || publicKey.Length != KeySize ||
            signature is null || signature.Length != SignatureSize ||
            message is null)
            return false;

        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(message, 0, message.Length);
            return verifier.VerifySignature(signature);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Hashing and key derivation

    public static byte[] Sha256(params byte[][] parts)
    {
        var digest = new Sha256Digest();
        foreach (var part in parts)
            digest.BlockUpdate(part, 0, part.Length);
        var output = new byte[HashSize];
        digest.DoFinal(output, 0);
        return output;
    }

    public static byte[] HmacSha256(byte[] key, params byte[][] parts)
    {
        var mac = new HMac(new Sha256Digest());
        mac.Init(new KeyParameter(key));
        foreach (var part in parts)
            mac.BlockUpdate(part, 0, part.Length);
        var output = new byte[mac.GetMacSize()];
        mac.DoFinal(output, 0);
        return output;
    }

    public static byte[] Hkdf(byte[] inputKey, byte[] salt, string info, int length = KeySize)
    {
        var generator = new HkdfBytesGenerator(new Sha256Digest());
        generator.Init(new HkdfParameters(inputKey, salt ?? [], Encoding.UTF8.GetBytes(info)));
        var output = new byte[length];
        generator.GenerateBytes(output, 0, length);
        return output;
    }

    // Authenticated encryption

    /// <summary>
    /// Encrypts with ChaCha20-Poly1305 and a random nonce.
    /// Output layout: nonce (12) || ciphertext || tag (16).
    /// </summary>
    public static byte[] Encrypt(byte[] key, byte[] plaintext, byte[] associatedData)
    {
        RequireLength(key, KeySize, "encryption key");
        var nonce = RandomBytes(NonceSize);
        var cipher = new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305();
        cipher.Init(true, new AeadParameters(new KeyParameter(key), TagSize * 8, nonce, associatedData ?? []));

        var output = new byte[NonceSize + cipher.GetOutputSize(plaintext.Length)];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        var written = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, NonceSize);
        cipher.DoFinal(output, NonceSize + written);
        return output;
    }

    public static byte[] Decrypt(byte[] key, byte[] encrypted, byte[] associatedData)
    {
        RequireLength(key, KeySize, "encryption key");
        if (encrypted is null || encrypted.Length < NonceSize + TagSize)
            throw new E2eeFormatException("Encrypted data is too short.");

        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(encrypted, 0, nonce, 0, NonceSize);
        var cipher = new Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305();
        cipher.Init(false, new AeadParameters(new KeyParameter(key), TagSize * 8, nonce, associatedData ?? []));

        var inputLength = encrypted.Length - NonceSize;
        var output = new byte[cipher.GetOutputSize(inputLength)];
        try
        {
            var written = cipher.ProcessBytes(encrypted, NonceSize, inputLength, output, 0);
            cipher.DoFinal(output, written);
        }
        catch (InvalidCipherTextException)
        {
            throw new E2eeVerificationException("Decryption failed.");
        }

        return output;
    }

    // Sealed boxes

    /// <summary>
    /// Encrypts to a recipient's X25519 public key with a fresh ephemeral key.
    /// Only the holder of the matching private key can open it. The context
    /// string binds the box to its purpose so a box cannot be replayed as
    /// another kind of data.
    /// Output layout: ephemeral public key (32) || encrypted payload.
    /// </summary>
    public static byte[] Seal(byte[] recipientPublicKey, byte[] plaintext, string context)
    {
        RequireLength(recipientPublicKey, KeySize, "recipient public key");
        var (ephemeralPrivate, ephemeralPublic) = GenerateX25519();
        var shared = X25519Agree(ephemeralPrivate, recipientPublicKey);
        var salt = Concat(ephemeralPublic, recipientPublicKey);
        var key = Hkdf(shared, salt, "valour-e2ee/seal/v1|" + context);
        var encrypted = Encrypt(key, plaintext, salt);
        return Concat(ephemeralPublic, encrypted);
    }

    public static byte[] Open(byte[] recipientPrivateKey, byte[] sealedData, string context)
    {
        RequireLength(recipientPrivateKey, KeySize, "recipient private key");
        if (sealedData is null || sealedData.Length < KeySize + NonceSize + TagSize)
            throw new E2eeFormatException("Sealed data is too short.");

        var ephemeralPublic = sealedData.AsSpan(0, KeySize).ToArray();
        var recipientPublic = X25519PublicKey(recipientPrivateKey);
        var shared = X25519Agree(recipientPrivateKey, ephemeralPublic);
        var salt = Concat(ephemeralPublic, recipientPublic);
        var key = Hkdf(shared, salt, "valour-e2ee/seal/v1|" + context);
        return Decrypt(key, sealedData.AsSpan(KeySize).ToArray(), salt);
    }

    // Helpers

    public static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a is null || b is null)
            return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static byte[] Concat(params byte[][] parts)
    {
        var length = parts.Sum(p => p.Length);
        var output = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, output, offset, part.Length);
            offset += part.Length;
        }
        return output;
    }

    public static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value ?? string.Empty);

    private static void RequireLength(byte[] value, int length, string name)
    {
        if (value is null || value.Length != length)
            throw new E2eeFormatException($"Invalid {name}.");
    }
}
