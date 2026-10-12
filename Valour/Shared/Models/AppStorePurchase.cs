namespace Valour.Shared.Models;

/// <summary>
/// A purchase made through App Store In-App Purchase that the iOS app reports
/// to the server for verification.
/// </summary>
public class AppStorePurchaseRequest
{
    /// <summary>The App Store product ID that was bought.</summary>
    public string ProductId { get; set; }

    /// <summary>The transaction ID StoreKit returned for the purchase.</summary>
    public string TransactionId { get; set; }
}

public static class AppStoreProducts
{
    /// <summary>
    /// Valour Credit packs sold as consumable in-app purchases, mapped to the
    /// number of credits each one deposits. The product IDs match Google Play.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> CreditPacks = GooglePlayProducts.CreditPacks;
}

/// <summary>
/// The app account token links an App Store purchase to a Valour account, the
/// way Google Play's obfuscated account ID does. Apple requires a UUID, so the
/// user ID fills the first eight bytes and a fixed marker fills the rest.
/// </summary>
public static class AppStoreAccountToken
{
    private static readonly byte[] Marker = [0x76, 0x61, 0x6c, 0x6f, 0x75, 0x72, 0x69, 0x64]; // "valourid"

    public static Guid FromUserId(long userId)
    {
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 8), userId);
        Marker.CopyTo(bytes, 8);
        return new Guid(bytes);
    }

    /// <summary>Returns the user ID in a token, or null if it is not a Valour token.</summary>
    public static long? ToUserId(string token)
    {
        if (!Guid.TryParse(token, out var guid))
            return null;

        var bytes = guid.ToByteArray();
        if (!bytes.AsSpan(8).SequenceEqual(Marker))
            return null;

        return BitConverter.ToInt64(bytes, 0);
    }
}
