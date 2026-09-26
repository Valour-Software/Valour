namespace Valour.Client.Device;

/// <summary>
/// Fingerprint sign-in. Native shells keep a key in the device's hardware
/// keystore that only signs after a fingerprint check. Web builds do not
/// register this service.
/// </summary>
public interface IDeviceKeyService
{
    /// <summary>True when the device can check a fingerprint and one is enrolled.</summary>
    bool IsAvailable { get; }

    /// <summary>A readable name for this device, shown in Connections.</summary>
    string DeviceName { get; }

    /// <summary>The account this device signs in to, or null when fingerprint sign-in is off.</summary>
    SavedDeviceKey Saved { get; }

    /// <summary>
    /// Creates a new key, replacing any earlier one, and returns its public key
    /// as base64 DER SubjectPublicKeyInfo.
    /// </summary>
    Task<string> CreateKeyAsync();

    /// <summary>
    /// Asks for a fingerprint and signs the challenge. Returns the base64 DER
    /// signature, or null when the person cancels or the key no longer works.
    /// </summary>
    Task<string> SignAsync(string challengeBase64, string title);

    void Save(SavedDeviceKey key);

    /// <summary>Deletes the key and forgets the account.</summary>
    void Clear();
}

/// <summary>The key ID the server issued and the account it belongs to.</summary>
public record SavedDeviceKey(string KeyId, long UserId, string UserName);
