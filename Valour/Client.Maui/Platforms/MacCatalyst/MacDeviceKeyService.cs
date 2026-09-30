using System.Security.Cryptography;
using System.Text.Json;
using Foundation;
using LocalAuthentication;
using Security;
using Valour.Client.Device;

namespace Valour.Client.Maui;

/// <summary>
/// Touch ID sign-in with a P-256 key in the Mac's Secure Enclave. The key can
/// only sign after a Touch ID check, it never leaves the Secure Enclave, and
/// adding or removing a fingerprint permanently disables it. The key lives in
/// the Keychain, so this needs a build signed with the Apple team.
/// </summary>
public class MacDeviceKeyService : IDeviceKeyService
{
    private const string SavedKey = "valour-device-key";
    private static readonly NSData KeyTag = NSData.FromString("gg.valour.app.signin");

    public bool IsAvailable
    {
        get
        {
#if VALOUR_UNSIGNED_KEYCHAIN
            return false;
#else
            using var context = new LAContext();
            return context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out _);
#endif
        }
    }

    public string DeviceName
    {
        get
        {
            var name = Microsoft.Maui.Devices.DeviceInfo.Current.Name?.Trim();
            return string.IsNullOrEmpty(name) ? "Mac" : name;
        }
    }

    public SavedDeviceKey Saved
    {
        get
        {
            var json = Preferences.Default.Get<string>(SavedKey, null);
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<SavedDeviceKey>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public void Save(SavedDeviceKey key) =>
        Preferences.Default.Set(SavedKey, JsonSerializer.Serialize(key));

    public void Clear()
    {
        Preferences.Default.Remove(SavedKey);
        SecKeyChain.Remove(KeyQuery());
    }

    public Task<string> CreateKeyAsync()
    {
        Clear();

        // Every signature needs its own Touch ID check, and enrolling or
        // removing a fingerprint invalidates the key.
        using var access = new SecAccessControl(SecAccessible.WhenPasscodeSetThisDeviceOnly,
            SecAccessControlCreateFlags.BiometryCurrentSet | SecAccessControlCreateFlags.PrivateKeyUsage);

        var parameters = new SecKeyGenerationParameters
        {
            KeyType = SecKeyType.ECSecPrimeRandom,
            KeySizeInBits = 256,
            TokenID = SecTokenID.SecureEnclave,
            PrivateKeyAttrs = new SecKeyParameters
            {
                IsPermanent = true,
                ApplicationTag = KeyTag,
                AccessControl = access,
            },
        };

        using var privateKey = SecKey.CreateRandomKey(parameters, out var error);
        using var publicKey = privateKey?.GetPublicKey();
        var point = publicKey?.GetExternalRepresentation()?.ToArray();
        if (error is not null || point is not { Length: 65 })
            return Task.FromResult<string>(null);

        // The Secure Enclave exports the uncompressed point (0x04, X, Y). The
        // server expects DER SubjectPublicKeyInfo.
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point[1..33], Y = point[33..] },
        });
        return Task.FromResult(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    public async Task<string> SignAsync(string challengeBase64, string title)
    {
        using var context = new LAContext();
        try
        {
            var (unlocked, _) = await MainThread.InvokeOnMainThreadAsync(() =>
                context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, title));
            if (!unlocked)
                return null;
        }
        catch (Exception)
        {
            return null;
        }

        // The query reuses the unlocked context, so signing doesn't ask again.
        var query = KeyQuery();
        query.AuthenticationContext = context;
        if (SecKeyChain.QueryAsConcreteType(query, out var status) is not SecKey key)
        {
            // A fingerprint was added or removed since the key was made, or
            // the key is gone.
            if (status == SecStatusCode.ItemNotFound)
                Clear();
            return null;
        }

        using (key)
        {
            using var signature = key.CreateSignature(SecKeyAlgorithm.EcdsaSignatureMessageX962Sha256,
                NSData.FromArray(Convert.FromBase64String(challengeBase64)), out var error);

            // X9.62 signatures are DER sequences, the format the server checks.
            return error is null && signature is not null ? Convert.ToBase64String(signature.ToArray()) : null;
        }
    }

    private static SecRecord KeyQuery() => new(SecKind.Key)
    {
        ApplicationTag = KeyTag,
        KeyType = SecKeyType.ECSecPrimeRandom,
    };
}
