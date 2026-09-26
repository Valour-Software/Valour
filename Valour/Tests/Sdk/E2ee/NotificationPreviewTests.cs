using System.Text.Json;
using Valour.Sdk.E2ee;

namespace Valour.Tests.Sdk.E2ee;

public class NotificationPreviewTests
{
    private const long PlanetId = 1234567890123456789;
    private const long ChannelId = 987654321098765432;

    private static (ChannelKeySecret Key, DeviceKeyPair Device) CreateKey(int generation = 1) =>
        (ChannelKeySecret.Generate(ChannelId, generation), DeviceKeyPair.Generate());

    private static byte[] Seal(ChannelKeySecret key, DeviceKeyPair device, string content, string embed = null,
        IReadOnlyList<byte[]> attachments = null, long planetId = PlanetId) =>
        MessageCrypto.Seal(key, planetId, 42, device, E2eeCrypto.RandomBytes(16), 0, 0, content, embed, [], 5000,
            attachments).Envelope;

    private static NotificationKeySet KeysFor(ChannelKeySecret key, long planetId = PlanetId)
    {
        var keys = new NotificationKeySet { UserId = "7" };
        keys.Add(planetId, key.ChannelId, key.Generation, key.ContentKey);
        return keys;
    }

    [Fact]
    public void TryDecrypt_ReadsTheTextWithAKeptContentKey()
    {
        var (key, device) = CreateKey();
        var envelope = Seal(key, device, "hello there");

        var preview = NotificationPreviewDecryptor.TryDecrypt(KeysFor(key), PlanetId, ChannelId, envelope);

        Assert.NotNull(preview);
        Assert.Equal("hello there", preview.Content);
        Assert.False(preview.HasEmbed);
        Assert.Equal(0, preview.AttachmentCount);
    }

    [Fact]
    public void TryDecrypt_ReturnsNullWithoutTheRightKey()
    {
        var (key, device) = CreateKey(2);
        var envelope = Seal(key, device, "hello");

        // Another generation of the same channel
        Assert.Null(NotificationPreviewDecryptor.TryDecrypt(KeysFor(ChannelKeySecret.Generate(ChannelId, 1)),
            PlanetId, ChannelId, envelope));

        // A different key stored under the right generation
        Assert.Null(NotificationPreviewDecryptor.TryDecrypt(KeysFor(ChannelKeySecret.Generate(ChannelId, 2)),
            PlanetId, ChannelId, envelope));

        Assert.Null(NotificationPreviewDecryptor.TryDecrypt(null, PlanetId, ChannelId, envelope));
        Assert.Null(NotificationPreviewDecryptor.TryDecrypt(KeysFor(key), PlanetId, ChannelId, null));
    }

    [Fact]
    public void TryDecrypt_ReturnsNullWhenThePayloadNamesAnotherChannel()
    {
        var (key, device) = CreateKey();
        var envelope = Seal(key, device, "hello");
        var keys = KeysFor(key);

        Assert.Null(NotificationPreviewDecryptor.TryDecrypt(keys, PlanetId, ChannelId + 1, envelope));
        Assert.Null(NotificationPreviewDecryptor.TryDecrypt(keys, PlanetId + 1, ChannelId, envelope));
    }

    [Fact]
    public void TryDecrypt_ReturnsNullForTamperedOrMalformedEnvelopes()
    {
        var (key, device) = CreateKey();
        var envelope = Seal(key, device, "hello");
        var keys = KeysFor(key);

        // The last bytes are the signature, which previews do not check, so
        // the byte changed is inside the encrypted body.
        var tampered = (byte[])envelope.Clone();
        tampered[^80] ^= 1;

        Assert.Null(NotificationPreviewDecryptor.TryDecrypt(keys, PlanetId, ChannelId, tampered));
        Assert.Null(NotificationPreviewDecryptor.TryDecrypt(keys, PlanetId, ChannelId, envelope[..40]));
        Assert.Null(NotificationPreviewDecryptor.TryDecrypt(keys, PlanetId, ChannelId, [1, 2, 3]));
    }

    [Fact]
    public void TryDecrypt_DescribesMessagesWithoutText()
    {
        var (key, device) = CreateKey();
        var keys = KeysFor(key);

        var files = NotificationPreviewDecryptor.TryDecrypt(keys, PlanetId, ChannelId,
            Seal(key, device, "", attachments: [E2eeCrypto.RandomBytes(32), E2eeCrypto.RandomBytes(32)]));
        var embed = NotificationPreviewDecryptor.TryDecrypt(keys, PlanetId, ChannelId,
            Seal(key, device, "", embed: "{}"));

        Assert.Equal("Sent 2 attachments", NotificationPreviewText.Describe(files));
        Assert.Equal("Sent an embed", NotificationPreviewText.Describe(embed));
    }

    [Fact]
    public void KeySet_KeepsTheNewestGenerationsPerChannel()
    {
        var keys = new NotificationKeySet { UserId = "7" };
        for (var generation = 1; generation <= 3; generation++)
            Assert.True(keys.Add(0, 5, generation, E2eeCrypto.RandomBytes(32)));

        Assert.Equal([3, 2], keys.Keys.Select(k => k.Generation));
        Assert.Null(keys.Find(0, 5, 1));

        // An older generation than every kept one is not added.
        Assert.False(keys.Add(0, 5, 1, E2eeCrypto.RandomBytes(32)));
    }

    [Fact]
    public void KeySet_ReportsNoChangeWhenTheChannelIsAlreadyFirst()
    {
        var keys = new NotificationKeySet { UserId = "7" };
        var first = E2eeCrypto.RandomBytes(32);
        var second = E2eeCrypto.RandomBytes(32);
        keys.Add(0, 5, 1, first);
        keys.Add(0, 5, 2, second);

        // A ring refresh adds the same generations again, oldest first.
        Assert.False(keys.Add(0, 5, 1, first));
        Assert.False(keys.Add(0, 5, 2, second));

        keys.Add(0, 6, 1, E2eeCrypto.RandomBytes(32));
        Assert.True(keys.Add(0, 5, 2, second));
        Assert.Equal("5", keys.Keys[0].ChannelId);
    }

    [Fact]
    public void KeySet_MergeKeepsItsOwnKeysFirst()
    {
        var mine = new NotificationKeySet { UserId = "7" };
        var ownKey = E2eeCrypto.RandomBytes(32);
        mine.Add(0, 5, 2, ownKey);

        var stored = new NotificationKeySet { UserId = "7" };
        stored.Add(0, 5, 1, E2eeCrypto.RandomBytes(32));
        stored.Add(0, 9, 1, E2eeCrypto.RandomBytes(32));

        mine.MergeFrom(stored);

        Assert.Equal(ownKey, mine.Find(0, 5, 2));
        Assert.Null(mine.Find(0, 5, 1));
        Assert.NotNull(mine.Find(0, 9, 1));
        Assert.Equal("5", mine.Keys[0].ChannelId);
    }

    [Fact]
    public void KeySet_DropsTheChannelsUsedLeastRecently()
    {
        var keys = new NotificationKeySet { UserId = "7" };
        for (var channel = 1; channel <= NotificationKeySet.MaxChannels; channel++)
        {
            keys.Add(0, channel, 1, E2eeCrypto.RandomBytes(32));
            keys.Add(0, channel, 2, E2eeCrypto.RandomBytes(32));
        }

        // Using channel 1 again moves it to the front, so channel 2 is dropped next.
        var channelOneKey = E2eeCrypto.RandomBytes(32);
        keys.Add(0, 1, 3, channelOneKey);
        keys.Add(0, NotificationKeySet.MaxChannels + 1, 1, E2eeCrypto.RandomBytes(32));

        Assert.Equal(NotificationKeySet.MaxChannels, keys.Keys.Select(k => k.ChannelId).Distinct().Count());
        Assert.Equal(channelOneKey, keys.Find(0, 1, 3));
        Assert.Null(keys.Find(0, 2, 2));
        Assert.NotNull(keys.Find(0, NotificationKeySet.MaxChannels + 1, 1));
    }

    [Fact]
    public void KeySet_KeepsPlanetChannelsWithTheSameIdApart()
    {
        // Channel IDs are unique only within one node.
        var keys = new NotificationKeySet { UserId = "7" };
        var first = E2eeCrypto.RandomBytes(32);
        var second = E2eeCrypto.RandomBytes(32);
        keys.Add(10, 5, 1, first);
        keys.Add(20, 5, 1, second);

        Assert.Equal(first, keys.Find(10, 5, 1));
        Assert.Equal(second, keys.Find(20, 5, 1));
        Assert.Null(keys.Find(0, 5, 1));
    }

    [Fact]
    public void KeySet_RoundTripsAndRefusesOtherFormats()
    {
        var key = E2eeCrypto.RandomBytes(32);
        var keys = new NotificationKeySet { UserId = "7" };
        keys.Add(PlanetId, ChannelId, 4, key);

        var parsed = NotificationKeySet.Parse(keys.Serialize());

        Assert.Equal("7", parsed.UserId);
        Assert.Equal(key, parsed.Find(PlanetId, ChannelId, 4));
        Assert.Null(NotificationKeySet.Parse("{\"v\":2,\"u\":\"7\",\"keys\":[]}"));
        Assert.Null(NotificationKeySet.Parse("not json"));
        Assert.Null(NotificationKeySet.Parse(null));
    }

    [Fact]
    public void SharedVector_DecryptsAndFormatsLikeTheServiceWorker()
    {
        using var stream = typeof(NotificationPreviewTests).Assembly
            .GetManifestResourceStream("Valour.Tests.notification-preview-vector.json")!;
        using var vector = JsonDocument.Parse(stream);
        var root = vector.RootElement;

        var keys = NotificationKeySet.Parse(root.GetProperty("keySet").GetString());
        var planetId = long.Parse(root.GetProperty("planetId").GetString()!);
        var channelId = long.Parse(root.GetProperty("channelId").GetString()!);

        foreach (var message in root.GetProperty("messages").EnumerateArray())
        {
            var preview = NotificationPreviewDecryptor.TryDecrypt(keys, planetId, channelId,
                Convert.FromBase64String(message.GetProperty("envelope").GetString()!));
            Assert.Equal(message.GetProperty("text").GetString(), NotificationPreviewText.Describe(preview));
        }

        foreach (var format in root.GetProperty("formatCases").EnumerateArray())
        {
            Assert.Equal(format.GetProperty("expected").GetString(),
                NotificationPreviewText.Format(format.GetProperty("input").GetString()));
        }
    }
}
