using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.E2ee;
using Valour.Sdk.Services;

namespace Valour.Tests.Apis;

/// <summary>
/// Stored key records the server cannot read, such as ones written by an
/// incompatible earlier build, must not break the channel's key routes.
/// </summary>
[Collection("ApiCollection")]
public class E2eeStoredRecordTests
{
    private readonly LoginTestFixture _fixture;

    public E2eeStoredRecordTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UnreadableKeyRecord_DoesNotBreakKeyRoutes()
    {
        var alice = await EncryptedChat.LoginAsync(_fixture, await _fixture.RegisterUser(), new MemoryE2eeKeyStore(),
            autoSetUp: true, requireReady: false);
        var bob = await EncryptedChat.LoginAsync(_fixture, await _fixture.RegisterUser(), new MemoryE2eeKeyStore(),
            autoSetUp: true, requireReady: false);
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        Assert.True((await EncryptedChat.SendAsync(alice, dm, "before the unreadable record")).Success);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            db.E2eeChannelKeyGenerations.Add(new Valour.Database.E2eeChannelKeyGeneration
            {
                ChannelId = dm.Id,
                Generation = 2,
                Body = new E2eeWriter().WriteMagic("VCK1").WriteInt64(dm.Id).ToArray(),
                Signature = new byte[E2eeCrypto.SignatureSize],
                CreatorUserId = alice.Me.Id,
                SealPublicKey = new byte[E2eeCrypto.KeySize],
                IndexGeneration = 2,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        foreach (var route in new[] { "keys", "recipients" })
        {
            using var response = await alice.PrimaryNode.HttpClient.GetAsync($"api/e2ee/channels/{dm.Id}/{route}");
            Assert.True(response.IsSuccessStatusCode, $"{route}: {(int)response.StatusCode}");
        }
    }
}
