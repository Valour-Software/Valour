using System.Text;

namespace Valour.Sdk.E2ee;

/// <summary>
/// Public keys that identify one of a user's devices. The device ID is derived
/// from the signing key so it cannot be reassigned to a different key.
/// </summary>
public sealed record DevicePublicKeys(string DeviceId, byte[] SignPublicKey, byte[] EncryptPublicKey)
{
    public static string DeriveDeviceId(byte[] signPublicKey) =>
        Base64Url.Encode(E2eeCrypto.Sha256(E2eeCrypto.Utf8("valour-e2ee/device-id/v1"), signPublicKey).AsSpan(0, 16));

    /// <summary>
    /// A short fingerprint of both device keys, used in device-linking QR codes.
    /// </summary>
    public byte[] Fingerprint() =>
        E2eeCrypto.Sha256(E2eeCrypto.Utf8("valour-e2ee/device-fingerprint/v1"), SignPublicKey, EncryptPublicKey)
            .AsSpan(0, 16).ToArray();

    public void Validate()
    {
        if (SignPublicKey?.Length != E2eeCrypto.KeySize || EncryptPublicKey?.Length != E2eeCrypto.KeySize)
            throw new E2eeFormatException("Invalid device public keys.");
        if (DeviceId != DeriveDeviceId(SignPublicKey))
            throw new E2eeVerificationException("Device ID does not match its signing key.");
    }
}

/// <summary>
/// The private keys of the device this client is running on. They never leave
/// the device.
/// </summary>
public sealed class DeviceKeyPair
{
    public string DeviceId { get; }
    public byte[] SignSeed { get; }
    public byte[] SignPublicKey { get; }
    public byte[] EncryptPrivateKey { get; }
    public byte[] EncryptPublicKey { get; }

    private DeviceKeyPair(byte[] signSeed, byte[] encryptPrivateKey)
    {
        SignSeed = signSeed;
        SignPublicKey = E2eeCrypto.Ed25519PublicKey(signSeed);
        EncryptPrivateKey = encryptPrivateKey;
        EncryptPublicKey = E2eeCrypto.X25519PublicKey(encryptPrivateKey);
        DeviceId = DevicePublicKeys.DeriveDeviceId(SignPublicKey);
    }

    public static DeviceKeyPair Generate() =>
        new(E2eeCrypto.RandomBytes(E2eeCrypto.KeySize), E2eeCrypto.RandomBytes(E2eeCrypto.KeySize));

    public DevicePublicKeys PublicKeys => new(DeviceId, SignPublicKey, EncryptPublicKey);

    public byte[] Sign(byte[] message) => E2eeCrypto.Sign(SignSeed, message);

    public byte[] Serialize() => new E2eeWriter()
        .WriteMagic("VDK1")
        .WriteFixed(SignSeed, E2eeCrypto.KeySize)
        .WriteFixed(EncryptPrivateKey, E2eeCrypto.KeySize)
        .ToArray();

    public static DeviceKeyPair Deserialize(byte[] data)
    {
        var reader = new E2eeReader(data);
        reader.ReadMagic("VDK1");
        var signSeed = reader.ReadFixed(E2eeCrypto.KeySize);
        var encryptPrivate = reader.ReadFixed(E2eeCrypto.KeySize);
        reader.EnsureEnd();
        return new DeviceKeyPair(signSeed, encryptPrivate);
    }

    /// <summary>
    /// Proves that the holder of this device's signing key agreed to join the
    /// given account. Without it, someone could list another person's device
    /// key under their own account.
    /// </summary>
    public byte[] CreateJoinProof(long userId) => Sign(DeviceJoinProof.Message(userId, PublicKeys));
}

public static class DeviceJoinProof
{
    public static byte[] Message(long userId, DevicePublicKeys device) => new E2eeWriter()
        .WriteMagic("VDJ1")
        .WriteInt64(userId)
        .WriteString(device.DeviceId)
        .WriteFixed(device.SignPublicKey, E2eeCrypto.KeySize)
        .WriteFixed(device.EncryptPublicKey, E2eeCrypto.KeySize)
        .ToArray();

    public static bool Verify(long userId, DevicePublicKeys device, byte[] proof) =>
        E2eeCrypto.Verify(device.SignPublicKey, Message(userId, device), proof);
}

/// <summary>
/// Public half of a user key, the key every one of a user's devices shares.
/// Channel keys are sealed to this key rather than to each device, so adding a
/// device does not require resharing every channel.
/// </summary>
public sealed record UserPublicKey(int Generation, byte[] SignPublicKey, byte[] EncryptPublicKey);

/// <summary>
/// The private user key. Each generation replaces the previous one when a
/// device is revoked. <see cref="PreviousWrapped"/> lets the holder of a newer
/// generation recover older ones, so history stays readable.
/// </summary>
public sealed class UserKeyPair
{
    public int Generation { get; }
    public byte[] SignSeed { get; }
    public byte[] SignPublicKey { get; }
    public byte[] EncryptPrivateKey { get; }
    public byte[] EncryptPublicKey { get; }

    private UserKeyPair(int generation, byte[] signSeed, byte[] encryptPrivateKey)
    {
        Generation = generation;
        SignSeed = signSeed;
        SignPublicKey = E2eeCrypto.Ed25519PublicKey(signSeed);
        EncryptPrivateKey = encryptPrivateKey;
        EncryptPublicKey = E2eeCrypto.X25519PublicKey(encryptPrivateKey);
    }

    public static UserKeyPair Generate(int generation) =>
        new(generation, E2eeCrypto.RandomBytes(E2eeCrypto.KeySize), E2eeCrypto.RandomBytes(E2eeCrypto.KeySize));

    public UserPublicKey PublicKey => new(Generation, SignPublicKey, EncryptPublicKey);

    public byte[] Serialize() => new E2eeWriter()
        .WriteMagic("VUK1")
        .WriteInt32(Generation)
        .WriteFixed(SignSeed, E2eeCrypto.KeySize)
        .WriteFixed(EncryptPrivateKey, E2eeCrypto.KeySize)
        .ToArray();

    public static UserKeyPair Deserialize(byte[] data)
    {
        var reader = new E2eeReader(data);
        reader.ReadMagic("VUK1");
        var generation = reader.ReadInt32();
        var signSeed = reader.ReadFixed(E2eeCrypto.KeySize);
        var encryptPrivate = reader.ReadFixed(E2eeCrypto.KeySize);
        reader.EnsureEnd();
        return new UserKeyPair(generation, signSeed, encryptPrivate);
    }

    public static string BoxContext(long userId, int generation, string recipientId) =>
        $"user-key|{userId}|{generation}|{recipientId}";

    /// <summary>
    /// Seals this key to a device or recovery key so that recipient can use it.
    /// </summary>
    public byte[] SealTo(long userId, string recipientId, byte[] recipientEncryptPublicKey) =>
        E2eeCrypto.Seal(recipientEncryptPublicKey, Serialize(), BoxContext(userId, Generation, recipientId));

    public static UserKeyPair OpenBox(long userId, int generation, string recipientId,
        byte[] recipientEncryptPrivateKey, byte[] box)
    {
        var key = Deserialize(E2eeCrypto.Open(recipientEncryptPrivateKey, box, BoxContext(userId, generation, recipientId)));
        if (key.Generation != generation)
            throw new E2eeVerificationException("User key box has the wrong generation.");
        return key;
    }

    /// <summary>
    /// Encrypts the previous generation under this one.
    /// </summary>
    public byte[] WrapPrevious(long userId, UserKeyPair previous) =>
        E2eeCrypto.Encrypt(PreviousWrapKey(), previous.Serialize(), PreviousAad(userId, Generation));

    public UserKeyPair UnwrapPrevious(long userId, byte[] wrapped)
    {
        var previous = Deserialize(E2eeCrypto.Decrypt(PreviousWrapKey(), wrapped, PreviousAad(userId, Generation)));
        if (previous.Generation != Generation - 1)
            throw new E2eeVerificationException("Wrapped user key has the wrong generation.");
        return previous;
    }

    private byte[] PreviousWrapKey() =>
        E2eeCrypto.Hkdf(E2eeCrypto.Concat(SignSeed, EncryptPrivateKey), null, "valour-e2ee/user-key/previous/v1");

    private static byte[] PreviousAad(long userId, int generation) =>
        new E2eeWriter().WriteMagic("VUP1").WriteInt64(userId).WriteInt32(generation).ToArray();
}

/// <summary>
/// A recovery key restores access when every device is lost. It is shown to
/// the user once as a code and never stored by Valour. Its signing and
/// encryption keys are derived from the code, so typing the code is enough to
/// act as a trusted device.
/// </summary>
public sealed class RecoveryKey
{
    public const int SecretLength = 20;

    /// <summary>
    /// Starts the device ID of every recovery key. Device IDs with it are
    /// reserved, so a device cannot pose as a recovery key.
    /// </summary>
    public const string IdPrefix = "recovery-";

    // Crockford base32 omits I, L, O, and U so codes are easy to read aloud.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public byte[] Secret { get; }
    public string RecoveryId { get; }
    public byte[] SignSeed { get; }
    public byte[] SignPublicKey { get; }
    public byte[] EncryptPrivateKey { get; }
    public byte[] EncryptPublicKey { get; }

    private RecoveryKey(byte[] secret)
    {
        Secret = secret;
        SignSeed = E2eeCrypto.Hkdf(secret, null, "valour-e2ee/recovery/sign/v1");
        EncryptPrivateKey = E2eeCrypto.Hkdf(secret, null, "valour-e2ee/recovery/encrypt/v1");
        SignPublicKey = E2eeCrypto.Ed25519PublicKey(SignSeed);
        EncryptPublicKey = E2eeCrypto.X25519PublicKey(EncryptPrivateKey);
        RecoveryId = IdPrefix + DevicePublicKeys.DeriveDeviceId(SignPublicKey);
    }

    public static RecoveryKey Generate() => new(E2eeCrypto.RandomBytes(SecretLength));

    public DevicePublicKeys PublicKeys => new(RecoveryId, SignPublicKey, EncryptPublicKey);

    public byte[] Sign(byte[] message) => E2eeCrypto.Sign(SignSeed, message);

    /// <summary>
    /// Formats the secret as eight groups of four characters.
    /// </summary>
    public string ToCode()
    {
        var chars = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var b in Secret)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                chars.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }

        if (bits > 0)
            chars.Append(Alphabet[(buffer << (5 - bits)) & 31]);

        var code = chars.ToString();
        return string.Join('-', Enumerable.Range(0, code.Length / 4).Select(i => code.Substring(i * 4, 4)));
    }

    /// <summary>
    /// Parses a code typed by a person. Case, spaces, and dashes are ignored,
    /// and characters that are easy to confuse are mapped to the digits they
    /// resemble.
    /// </summary>
    public static bool TryParse(string code, out RecoveryKey key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(code))
            return false;

        var normalized = new StringBuilder();
        foreach (var raw in code.ToUpperInvariant())
        {
            var c = raw switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => raw
            };

            if (c is '-' or ' ' or '\t' or '\n' or '\r')
                continue;
            if (Alphabet.IndexOf(c) < 0)
                return false;
            normalized.Append(c);
        }

        if (normalized.Length != SecretLength * 8 / 5)
            return false;

        var output = new byte[SecretLength];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        foreach (var c in normalized.ToString())
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output[index++] = (byte)((buffer >> bits) & 0xFF);
            }
        }

        key = new RecoveryKey(output);
        return true;
    }
}
