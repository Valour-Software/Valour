using Valour.Client.Storage;
using Valour.Client.Toast;
using Valour.Sdk.Client;
using Valour.Sdk.Services;

namespace Valour.Client.Utility;

/// <summary>
/// Keeps the admission key from an invite-only planet's invite link until the
/// person has joined the planet and this device can sign, then redeems it.
/// The key arrives in the link's fragment, which is lost on the way through
/// login, so it is stored locally in the meantime. Every way of joining with
/// an invite reports the join here.
/// </summary>
public class EncryptedInviteRedeemer
{
    private const string StorageKey = "pending-encrypted-invites";

    // Invites nobody joined with are forgotten after this long.
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(14);

    // A failed redemption is tried again on the next launch or verification,
    // up to this many times in all, since the failure may be a lost connection.
    private const int MaxAttempts = 3;

    private sealed class PendingInvite
    {
        public string InviteCode { get; set; }
        public string Fragment { get; set; }
        public long? PlanetId { get; set; }

        /// <summary>
        /// The account the invite belongs to, once known. An invite opened
        /// before login is claimed by the account that joins with it.
        /// </summary>
        public long? UserId { get; set; }

        public DateTime SavedAt { get; set; }

        public int Attempts { get; set; }
    }

    private readonly IAppStorage _storage;
    private readonly ValourClient _client;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public EncryptedInviteRedeemer(IAppStorage storage, ValourClient client)
    {
        _storage = storage;
        _client = client;
    }

    /// <summary>
    /// Returns the part of an invite link from the <c>#</c> on, or an empty
    /// string when it has none.
    /// </summary>
    public static string FragmentOf(string url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;
        var index = url.IndexOf('#');
        return index >= 0 ? url[index..] : string.Empty;
    }

    /// <summary>
    /// Remembers the key from an invite link's fragment, if it has one.
    /// </summary>
    public async Task RememberAsync(string inviteCode, string fragment)
    {
        if (string.IsNullOrEmpty(inviteCode) || !EncryptedInvite.TryParseFragment(fragment, out _))
            return;

        await _lock.WaitAsync();
        try
        {
            var pending = await LoadAsync();
            var userId = _client.Me?.Id;
            pending.RemoveAll(p => p.InviteCode == inviteCode && (p.UserId is null || p.UserId == userId));
            pending.Add(new PendingInvite
            {
                InviteCode = inviteCode,
                Fragment = fragment,
                UserId = userId,
                SavedAt = DateTime.UtcNow
            });
            await _storage.SetAsync(StorageKey, pending);
        }
        catch (Exception e)
        {
            Log($"Could not remember an invite key: {e.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Records that the person joined, or already belongs to, the planet an
    /// invite is for, then redeems it if this device can.
    /// </summary>
    public async Task OnJoinedAsync(string inviteCode, long planetId)
    {
        var userId = _client.Me?.Id;
        if (userId is null)
            return;

        var found = false;
        await _lock.WaitAsync();
        try
        {
            var pending = await LoadAsync();
            foreach (var entry in pending.Where(p => p.InviteCode == inviteCode && (p.UserId is null || p.UserId == userId)))
            {
                entry.PlanetId = planetId;
                entry.UserId = userId;
                found = true;
            }

            if (found)
                await _storage.SetAsync(StorageKey, pending);
        }
        catch (Exception e)
        {
            Log($"Could not record joining with an invite: {e.Message}");
        }
        finally
        {
            _lock.Release();
        }

        if (found)
            await RedeemPendingAsync();
    }

    /// <summary>
    /// Redeems every remembered invite for a planet this account joined.
    /// Called again once this device is verified. An invite that cannot be
    /// redeemed is tried again a few times, then dropped with a notice rather
    /// than retried on every launch; an admin can still admit the person.
    /// </summary>
    public async Task RedeemPendingAsync()
    {
        var userId = _client.Me?.Id;
        if (userId is null || _client.E2eeService.Status != E2eeStatus.Ready)
            return;

        await _lock.WaitAsync();
        try
        {
            List<PendingInvite> pending;
            try
            {
                pending = await LoadAsync();
            }
            catch (Exception e)
            {
                // Unreadable storage would otherwise fail on every launch.
                Log($"Pending invite keys could not be read and were cleared: {e.Message}");
                await _storage.RemoveAsync(StorageKey);
                return;
            }

            if (pending.Count == 0)
                return;

            var changed = pending.RemoveAll(p => DateTime.UtcNow - p.SavedAt > PendingLifetime) > 0;

            foreach (var entry in pending.Where(p => p.PlanetId is not null && p.UserId == userId).ToList())
            {
                changed = true;
                entry.Attempts++;
                var lastAttempt = entry.Attempts >= MaxAttempts;

                bool done;
                try
                {
                    done = await RedeemAsync(entry, lastAttempt);
                }
                catch (Exception e)
                {
                    Log($"Redeeming an invite key failed: {e}");
                    done = lastAttempt;
                    if (done)
                        Notify("Couldn't use the invite's key",
                            "An admin of the planet needs to let you in instead.", false);
                }

                if (done)
                    pending.Remove(entry);
            }

            if (changed)
                await _storage.SetAsync(StorageKey, pending);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Tries to redeem an invite. Returns true when it is finished with,
    /// because it worked or this was the last attempt.
    /// </summary>
    private async Task<bool> RedeemAsync(PendingInvite entry, bool lastAttempt)
    {
        if (!EncryptedInvite.TryParseFragment(entry.Fragment, out var invite))
            return true;

        var planet = await _client.PlanetService.FetchPlanetAsync(entry.PlanetId!.Value);
        if (planet is null)
        {
            if (lastAttempt)
                Notify("Couldn't use the invite's key",
                    "The planet could not be loaded. An admin of the planet needs to let you in instead.",
                    false);
            return lastAttempt;
        }

        var result = await _client.E2eeService.RedeemPlanetInviteAsync(planet, invite);
        if (result.Success)
        {
            Notify($"You're in {planet.Name}",
                "The invite let you in, so you can read and send encrypted messages there.", true);
            return true;
        }

        Log($"Redeeming the invite for planet {planet.Id} failed: {result.Message}");
        if (lastAttempt)
            Notify($"Couldn't use the invite for {planet.Name}",
                $"{result.Message} An admin of the planet needs to let you in instead.", false);
        return lastAttempt;
    }

    private static void Notify(string title, string message, bool success) =>
        ToastContainer.Instance?.AddToast(new ToastData(title, message,
            success ? ToastProgressState.Success : ToastProgressState.Failure));

    private void Log(string message) =>
        _client.Logger.Log<EncryptedInviteRedeemer>(message, "red");

    private async Task<List<PendingInvite>> LoadAsync() =>
        await _storage.GetAsync<List<PendingInvite>>(StorageKey) ?? new List<PendingInvite>();
}
