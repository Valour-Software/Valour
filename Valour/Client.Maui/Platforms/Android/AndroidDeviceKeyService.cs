using System.Text.Json;
using Android.Runtime;
using Android.Security.Keystore;
using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using Java.Security;
using Java.Security.Spec;
using Valour.Client.Device;

namespace Valour.Client.Maui;

/// <summary>
/// Fingerprint sign-in with a P-256 key in the Android keystore. The key can
/// only sign after a strong biometric check, it never leaves the device's
/// secure hardware, and enrolling a new fingerprint permanently disables it.
/// </summary>
public class AndroidDeviceKeyService : IDeviceKeyService
{
    private const string KeyAlias = "valour-signin";
    private const string SavedKey = "valour-device-key";

    public bool IsAvailable =>
        BiometricManager.From(Platform.AppContext).CanAuthenticate(BiometricManager.Authenticators.BiometricStrong)
        == BiometricManager.BiometricSuccess;

    public string DeviceName
    {
        get
        {
            var manufacturer = Microsoft.Maui.Devices.DeviceInfo.Current.Manufacturer?.Trim();
            var model = Microsoft.Maui.Devices.DeviceInfo.Current.Model?.Trim() ?? "Android device";
            return string.IsNullOrEmpty(manufacturer) || model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)
                ? model
                : $"{manufacturer} {model}";
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
        try
        {
            var keyStore = LoadKeyStore();
            if (keyStore.ContainsAlias(KeyAlias))
                keyStore.DeleteEntry(KeyAlias);
        }
        catch (Java.Lang.Exception)
        {
            // Nothing to delete.
        }
    }

    public Task<string> CreateKeyAsync()
    {
        try
        {
            Clear();

            var builder = new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Sign)
                .SetAlgorithmParameterSpec(new ECGenParameterSpec("secp256r1"))
                .SetDigests(KeyProperties.DigestSha256)
                .SetUserAuthenticationRequired(true)
                .SetInvalidatedByBiometricEnrollment(true);

            // Every signature needs its own fingerprint check.
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
                builder.SetUserAuthenticationParameters(0, (int)KeyPropertiesAuthType.BiometricStrong);

            var generator = KeyPairGenerator.GetInstance(KeyProperties.KeyAlgorithmEc, "AndroidKeyStore")!;
            generator.Initialize(builder.Build());
            var pair = generator.GenerateKeyPair()!;

            // X.509 encoding is DER SubjectPublicKeyInfo, which the server expects.
            return Task.FromResult(Convert.ToBase64String(pair.Public!.GetEncoded()!));
        }
        catch (Java.Lang.Exception)
        {
            return Task.FromResult<string>(null);
        }
    }

    public async Task<string> SignAsync(string challengeBase64, string title)
    {
        Signature signature;
        try
        {
            // A C# cast can't see Java interfaces on the keystore's key object.
            var key = LoadKeyStore().GetKey(KeyAlias, null);
            if (key is null)
                return null;
            var privateKey = key.JavaCast<IPrivateKey>();

            signature = Signature.GetInstance("SHA256withECDSA")!;
            signature.InitSign(privateKey);
        }
        catch (KeyPermanentlyInvalidatedException)
        {
            // A fingerprint was added or removed since the key was made.
            Clear();
            return null;
        }
        catch (Java.Lang.Exception)
        {
            return null;
        }

        var unlocked = new TaskCompletionSource<Signature>();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Platform.CurrentActivity is not FragmentActivity activity)
            {
                unlocked.TrySetResult(null);
                return;
            }

            var prompt = new BiometricPrompt(activity, ContextCompat.GetMainExecutor(activity), new PromptCallback(unlocked));
            var info = new BiometricPrompt.PromptInfo.Builder()
                .SetTitle(title)
                .SetNegativeButtonText("Cancel")
                .SetAllowedAuthenticators(BiometricManager.Authenticators.BiometricStrong)
                .Build();

            prompt.Authenticate(info, new BiometricPrompt.CryptoObject(signature));
        });

        var ready = await unlocked.Task;
        if (ready is null)
            return null;

        ready.Update(Convert.FromBase64String(challengeBase64));
        return Convert.ToBase64String(ready.Sign()!);
    }

    private static KeyStore LoadKeyStore()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore")!;
        keyStore.Load(null);
        return keyStore;
    }

    private sealed class PromptCallback(TaskCompletionSource<Signature> unlocked) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result) =>
            unlocked.TrySetResult(result.CryptoObject?.Signature);

        // Cancelled, locked out, or no hardware. A finger that doesn't match
        // calls OnAuthenticationFailed instead and the prompt stays open.
        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString) =>
            unlocked.TrySetResult(null);
    }
}
