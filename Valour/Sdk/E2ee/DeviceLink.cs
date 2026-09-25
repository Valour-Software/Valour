using System.Text;

namespace Valour.Sdk.E2ee;

/// <summary>
/// How a device-link session was started. In both modes one device shows a
/// secret that the other device reads from its screen, so the server, which
/// relays everything else, never learns it.
/// </summary>
public enum DeviceLinkMode
{
    /// <summary>
    /// The new device shows a QR code containing a fingerprint of its keys and
    /// a secret. An existing device scans it, checks the fingerprint, and
    /// proves with the secret that it made the approval.
    /// </summary>
    NewDeviceShowsCode = 0,

    /// <summary>
    /// An existing device shows a QR code, and a code to type, containing a
    /// secret. The new device scans or types it and proves it saw the secret;
    /// the existing device proves the same when it approves.
    /// </summary>
    ExistingDeviceShowsCode = 1
}

/// <summary>
/// The contents of a device-link QR code. <see cref="Fingerprint"/> is set
/// only when the new device shows the code.
/// </summary>
public sealed record DeviceLinkCode(DeviceLinkMode Mode, long UserId, string SessionId, byte[] Fingerprint,
    byte[] Secret)
{
    private const string Prefix = "valour-link";
    private const int Version = 2;

    /// <summary>The length of the secret in a code shown by a new device.</summary>
    public const int NewDeviceSecretLength = 16;

    /// <summary>
    /// The length of the secret in a code shown by an existing device. It is
    /// short enough to type as 16 characters and long enough (80 bits) that
    /// the server cannot find it by trying every value against the proofs it
    /// relays.
    /// </summary>
    public const int ExistingDeviceSecretLength = 10;

    private const int FingerprintLength = 16;

    public override string ToString()
    {
        var value = Mode == DeviceLinkMode.NewDeviceShowsCode
            ? E2eeCrypto.Concat(Fingerprint, Secret)
            : Secret;
        return $"{Prefix}:{Version}:{(Mode == DeviceLinkMode.NewDeviceShowsCode ? "n" : "e")}:{UserId}:{SessionId}:{Base64Url.Encode(value)}";
    }

    public static bool TryParse(string text, out DeviceLinkCode code)
    {
        code = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split(':');
        if (parts.Length != 6 || parts[0] != Prefix || parts[1] != Version.ToString())
            return false;

        var mode = parts[2] switch
        {
            "n" => DeviceLinkMode.NewDeviceShowsCode,
            "e" => DeviceLinkMode.ExistingDeviceShowsCode,
            _ => (DeviceLinkMode?)null
        };

        if (mode is null || !long.TryParse(parts[3], out var userId) || string.IsNullOrWhiteSpace(parts[4]))
            return false;

        try
        {
            var value = Base64Url.Decode(parts[5]);
            if (mode == DeviceLinkMode.NewDeviceShowsCode)
            {
                if (value.Length != FingerprintLength + NewDeviceSecretLength)
                    return false;
                code = new DeviceLinkCode(mode.Value, userId, parts[4], value[..FingerprintLength],
                    value[FingerprintLength..]);
            }
            else
            {
                if (value.Length != ExistingDeviceSecretLength)
                    return false;
                code = new DeviceLinkCode(mode.Value, userId, parts[4], null, value);
            }

            return true;
        }
        catch (Exception e) when (e is E2eeFormatException or FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// The code an existing device shows for a person to type on a new device
/// that has no camera: the 10-byte secret as 16 Crockford base32 characters
/// in four groups.
/// </summary>
public static class DeviceLinkTypedCode
{
    // Crockford base32 omits I, L, O, and U so codes are easy to read aloud.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Characters = DeviceLinkCode.ExistingDeviceSecretLength * 8 / 5;

    public static string Format(byte[] secret)
    {
        if (secret?.Length != DeviceLinkCode.ExistingDeviceSecretLength)
            throw new E2eeFormatException("Invalid link secret.");

        var chars = new StringBuilder(Characters);
        var buffer = 0;
        var bits = 0;
        foreach (var b in secret)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                chars.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }

        var code = chars.ToString();
        return string.Join('-', Enumerable.Range(0, Characters / 4).Select(i => code.Substring(i * 4, 4)));
    }

    /// <summary>
    /// Parses a code typed by a person. Case, spaces, and dashes are ignored,
    /// and characters that are easy to confuse are mapped to the digits they
    /// resemble.
    /// </summary>
    public static bool TryParse(string text, out byte[] secret)
    {
        secret = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = new StringBuilder();
        foreach (var raw in text.ToUpperInvariant())
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

        if (normalized.Length != Characters)
            return false;

        var output = new byte[DeviceLinkCode.ExistingDeviceSecretLength];
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

        secret = output;
        return true;
    }
}

/// <summary>
/// Checks both devices make during linking. The server relays device keys,
/// the signed key log entry, and the user key box, so without these checks it
/// could substitute a device it controls on either side. Each check uses the
/// fingerprint or secret one device read from the other's screen.
/// </summary>
public static class DeviceLinkVerification
{
    /// <summary>
    /// Checks the key fingerprint from a QR code shown by the new device.
    /// </summary>
    public static bool MatchesFingerprint(DevicePublicKeys device, byte[] fingerprint) =>
        E2eeCrypto.FixedTimeEquals(device.Fingerprint(), fingerprint);

    /// <summary>
    /// The session ID for a code shown by an existing device. It is derived
    /// from the secret, so a new device that only has the typed code can find
    /// the session.
    /// </summary>
    public static string SessionIdFor(byte[] secret, long userId) =>
        Base64Url.Encode(E2eeCrypto.HmacSha256(secret, new E2eeWriter()
            .WriteMagic("VLI1")
            .WriteInt64(userId)
            .ToArray()).AsSpan(0, 16));

    /// <summary>
    /// Proves the new device read the secret shown by the existing device.
    /// </summary>
    public static byte[] JoinMac(byte[] secret, long userId, string sessionId, DevicePublicKeys device) =>
        E2eeCrypto.HmacSha256(secret, new E2eeWriter()
            .WriteMagic("VLJ1")
            .WriteInt64(userId)
            .WriteString(sessionId)
            .WriteString(device.DeviceId)
            .WriteFixed(device.SignPublicKey, E2eeCrypto.KeySize)
            .WriteFixed(device.EncryptPublicKey, E2eeCrypto.KeySize)
            .ToArray());

    public static bool VerifyJoinMac(byte[] secret, long userId, string sessionId, DevicePublicKeys device, byte[] mac) =>
        E2eeCrypto.FixedTimeEquals(JoinMac(secret, userId, sessionId, device), mac);

    /// <summary>
    /// Proves an approval came from the device that read or showed the
    /// secret. It covers exactly what the new device accepts: the signed
    /// <c>AddDevice</c> entry body, which chains to the rest of the key log,
    /// the user key box, and the sealed pins. A reset the server forged gives
    /// the server a device that can sign entries, but not the secret.
    /// </summary>
    public static byte[] ApprovalMac(byte[] secret, long userId, string sessionId, string deviceId, byte[] entryBody,
        int boxGeneration, byte[] box, byte[] sealedPins) =>
        E2eeCrypto.HmacSha256(secret, new E2eeWriter()
            .WriteMagic("VLA1")
            .WriteInt64(userId)
            .WriteString(sessionId)
            .WriteString(deviceId)
            .WriteBytes(entryBody ?? [])
            .WriteInt32(boxGeneration)
            .WriteBytes(box ?? [])
            .WriteBytes(sealedPins ?? [])
            .ToArray());

    public static bool VerifyApprovalMac(byte[] secret, long userId, string sessionId, string deviceId,
        byte[] entryBody, int boxGeneration, byte[] box, byte[] sealedPins, byte[] mac) =>
        mac?.Length == E2eeCrypto.HashSize &&
        E2eeCrypto.FixedTimeEquals(
            ApprovalMac(secret, userId, sessionId, deviceId, entryBody, boxGeneration, box, sealedPins), mac);

    /// <summary>
    /// The context for pins the approving device seals to the new device.
    /// </summary>
    public static string PinsContext(long userId, string sessionId, string deviceId) =>
        $"valour-e2ee/link-pins/v1|{userId}|{sessionId}|{deviceId}";
}
