namespace Valour.Client.Device;

/// <summary>
/// Sign-in with the device's fingerprint or face check. Native shells keep a
/// key in the device's secure hardware that only signs after that check. Web
/// builds do not register this service.
/// </summary>
public interface IDeviceKeyService
{
    /// <summary>True when the device can run its check and a fingerprint or face is enrolled.</summary>
    bool IsAvailable { get; }

    /// <summary>The check this device uses, which decides the wording shown to the person.</summary>
    DeviceKeyMethod Method { get; }

    /// <summary>A readable name for this device, shown in Security settings.</summary>
    string DeviceName { get; }

    /// <summary>The account this device signs in to, or null when device sign-in is off.</summary>
    SavedDeviceKey Saved { get; }

    /// <summary>
    /// Creates a new key, replacing any earlier one, and returns its public key
    /// as base64 DER SubjectPublicKeyInfo.
    /// </summary>
    Task<string> CreateKeyAsync();

    /// <summary>
    /// Runs the device's check and signs the challenge. Returns the base64 DER
    /// signature, or null when the person cancels or the key no longer works.
    /// </summary>
    Task<string> SignAsync(string challengeBase64, string title);

    void Save(SavedDeviceKey key);

    /// <summary>Deletes the key and forgets the account.</summary>
    void Clear();
}

/// <summary>The key ID the server issued and the account it belongs to.</summary>
public record SavedDeviceKey(string KeyId, long UserId, string UserName);

/// <summary>How a device confirms it's the person before its key signs.</summary>
public enum DeviceKeyMethod
{
    Fingerprint,
    TouchId,
    FaceId,
}
