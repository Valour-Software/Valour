namespace Valour.Client.Device;

/// <summary>
/// Wording and icons for device sign-in, so screens name the check the device
/// actually uses: "fingerprint" on Android, "Touch ID" or "Face ID" on Apple
/// devices.
/// </summary>
public static class DeviceKeyText
{
    /// <summary>The check's name inside a sentence, as in "Confirm with Face ID".</summary>
    public static string Name(DeviceKeyMethod method) => method switch
    {
        DeviceKeyMethod.TouchId => "Touch ID",
        DeviceKeyMethod.FaceId => "Face ID",
        _ => "fingerprint",
    };

    /// <summary>The check's name at the start of a heading, as in "Fingerprint sign-in".</summary>
    public static string Title(DeviceKeyMethod method)
    {
        var name = Name(method);
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>The check as it reads after "with", as in "sign in with your fingerprint".</summary>
    public static string With(DeviceKeyMethod method) =>
        method == DeviceKeyMethod.Fingerprint ? "your fingerprint" : Name(method);

    /// <summary>A Bootstrap icon name for the check.</summary>
    public static string Icon(DeviceKeyMethod method) =>
        method == DeviceKeyMethod.FaceId ? "person-bounding-box" : "fingerprint";
}
