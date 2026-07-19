using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace Valour.Server.Services;

/// <summary>
/// Supplies a 256-bit key-encryption key (KEK) for wrapping the Data Protection
/// key ring at rest, loaded from configuration OUTSIDE the database:
///   DataProtection:Kek       — base64 of 32 random bytes, or
///   DataProtection:KekFile   — path to a file whose contents are the base64 KEK.
///
/// Without this, the DP ring (which in turn protects federation signing keys and
/// planet BYO credentials) sits unencrypted in the same DB as the ciphertext, so
/// a database reader recovers everything. With it, a DB reader cannot decrypt the
/// ring without also holding the out-of-band KEK.
/// </summary>
public sealed class DataProtectionKekProvider
{
    public byte[] Key { get; }
    public bool Available => Key is not null;

    public DataProtectionKekProvider(IConfiguration configuration, ILogger<DataProtectionKekProvider> logger)
    {
        var inline = configuration["DataProtection:Kek"];
        var file = configuration["DataProtection:KekFile"];

        string material = null;
        if (!string.IsNullOrWhiteSpace(inline))
            material = inline.Trim();
        else if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
            material = File.ReadAllText(file).Trim();

        if (string.IsNullOrWhiteSpace(material))
        {
            logger.LogWarning(
                "No DataProtection KEK configured (DataProtection:Kek / DataProtection:KekFile). The key ring is stored " +
                "unwrapped in the database — a database reader can recover federation signing keys and planet credentials. " +
                "Set a KEK on any deployment where that matters.");
            return;
        }

        byte[] bytes;
        try { bytes = Convert.FromBase64String(material); }
        catch
        {
            throw new InvalidOperationException("DataProtection KEK must be base64. Generate one with: openssl rand -base64 32");
        }

        if (bytes.Length != 32)
            throw new InvalidOperationException($"DataProtection KEK must decode to 32 bytes (got {bytes.Length}). Generate: openssl rand -base64 32");

        Key = bytes;
        logger.LogInformation("Data Protection key ring is wrapped with an external KEK.");
    }
}

/// <summary>AES-GCM wrap of the DP key ring XML with the external KEK.</summary>
public sealed class KekXmlEncryptor : IXmlEncryptor
{
    private readonly DataProtectionKekProvider _kek;
    public KekXmlEncryptor(DataProtectionKekProvider kek) => _kek = kek;

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];

        using (var aes = new AesGcm(_kek.Key, tag.Length))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var blob = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, blob, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, blob, nonce.Length + tag.Length, ciphertext.Length);

        var element = new XElement("encryptedKey",
            new XComment(" This key ring entry is wrapped with the external DataProtection KEK. "),
            new XElement("value", Convert.ToBase64String(blob)));

        return new EncryptedXmlInfo(element, typeof(KekXmlDecryptor));
    }
}

/// <summary>AES-GCM unwrap; DI-activated by Data Protection when reading keys.</summary>
public sealed class KekXmlDecryptor : IXmlDecryptor
{
    private readonly DataProtectionKekProvider _kek;
    public KekXmlDecryptor(DataProtectionKekProvider kek) => _kek = kek;

    public XElement Decrypt(XElement encryptedElement)
    {
        if (_kek?.Available != true)
            throw new InvalidOperationException(
                "The Data Protection key ring is KEK-wrapped but no KEK is configured. Set DataProtection:Kek (or :KekFile) to the same value used to write it.");

        var blob = Convert.FromBase64String(encryptedElement.Element("value")!.Value);
        var nonce = blob.AsSpan(0, 12);
        var tag = blob.AsSpan(12, 16);
        var ciphertext = blob.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];

        using (var aes = new AesGcm(_kek.Key, 16))
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return XElement.Parse(Encoding.UTF8.GetString(plaintext));
    }
}
