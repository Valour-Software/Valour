using System.Text.Json;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;

namespace Valour.Sdk.Services;

public partial class E2eeService
{
    /// <summary>
    /// A signed invite's secret, kept on the device that created it so the
    /// full link can be copied again later.
    /// </summary>
    private sealed class SavedInvite
    {
        public string Secret { get; set; }

        /// <summary>When the invite expires, in Unix milliseconds, or 0 for never.</summary>
        public long ExpiresMs { get; set; }
    }

    private const string InviteSecretsKey = "invite-secrets";

    private readonly SemaphoreSlim _inviteSecretsLock = new(1, 1);

    private static string SavedInviteKey(long planetId, string inviteCode) => $"{planetId}/{inviteCode}";

    /// <summary>
    /// The part to append to an invite link so it lets people in right away,
    /// when this device signed the invite code and its signed invite is still
    /// usable, or null otherwise. The secret lives only in this device's key
    /// store, so other devices and other admins copy the plain link.
    /// </summary>
    public async Task<string> GetSavedInviteFragmentAsync(Planet planet, string inviteCode)
    {
        if (_client.Me is null || string.IsNullOrEmpty(inviteCode))
            return null;

        var saved = (await LoadSavedInvitesAsync()).GetValueOrDefault(SavedInviteKey(planet.Id, inviteCode));
        if (saved is null)
            return null;

        // The signed invite may have been revoked from another device.
        var log = await GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node);
        if (log is not { IsGoverning: true } || !log.Invites.TryGetValue(inviteCode, out var invite) ||
            !invite.IsUsable(NowMs()))
        {
            if (log is not null)
                await ForgetInviteSecretAsync(planet.Id, inviteCode);
            return null;
        }

        try
        {
            return new EncryptedInvite { InviteId = inviteCode, Secret = Base64Url.Decode(saved.Secret) }.Fragment;
        }
        catch (E2eeFormatException)
        {
            await ForgetInviteSecretAsync(planet.Id, inviteCode);
            return null;
        }
    }

    private async Task SaveInviteSecretAsync(long planetId, EncryptedInvite invite, DateTime? expires)
    {
        await _inviteSecretsLock.WaitAsync();
        try
        {
            var saved = await LoadSavedInvitesAsync();
            saved[SavedInviteKey(planetId, invite.InviteId)] = new SavedInvite
            {
                Secret = Base64Url.Encode(invite.Secret),
                ExpiresMs = expires is null ? 0 : new DateTimeOffset(expires.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
            };
            await SaveSavedInvitesAsync(saved);
        }
        finally
        {
            _inviteSecretsLock.Release();
        }
    }

    private async Task ForgetInviteSecretAsync(long planetId, string inviteCode)
    {
        await _inviteSecretsLock.WaitAsync();
        try
        {
            var saved = await LoadSavedInvitesAsync();
            if (saved.Remove(SavedInviteKey(planetId, inviteCode)))
                await SaveSavedInvitesAsync(saved);
        }
        finally
        {
            _inviteSecretsLock.Release();
        }
    }

    /// <summary>
    /// Forgets every saved invite secret, when this device's keys are
    /// removed or started over.
    /// </summary>
    private Task ForgetInviteSecretsAsync() => Store.RemoveAsync(StoreKey(InviteSecretsKey));

    /// <summary>Loads saved invite secrets, leaving out expired ones.</summary>
    private async Task<Dictionary<string, SavedInvite>> LoadSavedInvitesAsync()
    {
        var saved = await LoadStoredJsonAsync<Dictionary<string, SavedInvite>>(InviteSecretsKey);
        var now = NowMs();
        foreach (var key in saved.Where(x => x.Value.ExpiresMs != 0 && x.Value.ExpiresMs <= now).Select(x => x.Key)
                     .ToList())
            saved.Remove(key);
        return saved;
    }

    private Task SaveSavedInvitesAsync(Dictionary<string, SavedInvite> saved) =>
        saved.Count == 0
            ? Store.RemoveAsync(StoreKey(InviteSecretsKey))
            : Store.SetAsync(StoreKey(InviteSecretsKey), JsonSerializer.SerializeToUtf8Bytes(saved));
}
