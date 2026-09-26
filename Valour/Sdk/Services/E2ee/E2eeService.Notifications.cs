using Valour.Sdk.E2ee;
using Valour.Sdk.Models;

namespace Valour.Sdk.Services;

public partial class E2eeService
{
    private static readonly TimeSpan NotificationKeysSaveDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Where the message keys for notification previews are kept, so that
    /// notifications arriving while the app is closed can show message text.
    /// Null, the default, keeps none. See <see cref="INotificationKeyStore"/>.
    /// </summary>
    public INotificationKeyStore NotificationKeyStore { get; set; }

    private readonly SemaphoreSlim _notificationKeysLock = new(1, 1);
    private NotificationKeySet _notificationKeys;
    private int _notificationKeysSaveScheduled;

    // Whether _notificationKeys was read from or written to the store, so a
    // store that is empty now was cleared by another instance of the app.
    private bool _notificationKeysStored;

    /// <summary>
    /// Keeps the content keys of a channel's newest generations after the
    /// ring that holds them was verified.
    /// </summary>
    private void RememberNotificationKeys(Channel channel, ChannelKeyRing ring)
    {
        if (NotificationKeyStore is null || _client.Me is null || ring is null)
            return;

        var secrets = ring.Secrets.Values
            .Where(s => s.Generation >= ring.LatestGeneration - (NotificationKeySet.GenerationsPerChannel - 1))
            .OrderBy(s => s.Generation)
            .ToList();
        if (secrets.Count == 0)
            return;

        var userId = _client.Me.Id;
        var planetId = channel.PlanetId ?? 0;
        var session = _session.Token;
        _ = RunInBackgroundAsync(() => AddNotificationKeysAsync(userId, planetId, channel.Id, secrets, session),
            $"Keeping notification keys for channel {channel.Id} failed");
    }

    private async Task AddNotificationKeysAsync(long userId, long planetId, long channelId,
        List<ChannelKeySecret> secrets, CancellationToken session)
    {
        var changed = false;
        await _notificationKeysLock.WaitAsync();
        try
        {
            if (session.IsCancellationRequested)
                return;

            var keys = await LoadNotificationKeysAsync(userId);
            foreach (var secret in secrets)
                changed |= keys.Add(planetId, channelId, secret.Generation, secret.ContentKey);
        }
        finally
        {
            _notificationKeysLock.Release();
        }

        if (changed && Interlocked.Exchange(ref _notificationKeysSaveScheduled, 1) == 0)
            _ = RunInBackgroundAsync(() => SaveNotificationKeysAsync(session), "Saving notification keys failed");
    }

    private async Task<NotificationKeySet> LoadNotificationKeysAsync(long userId)
    {
        var user = userId.ToString();
        if (_notificationKeys?.UserId == user)
            return _notificationKeys;

        var stored = NotificationKeySet.Parse(await NotificationKeyStore.LoadAsync());
        _notificationKeysStored = stored?.UserId == user;
        _notificationKeys = _notificationKeysStored ? stored : new NotificationKeySet { UserId = user };
        return _notificationKeys;
    }

    // Changes close together are written once.
    private async Task SaveNotificationKeysAsync(CancellationToken session)
    {
        try
        {
            await Task.Delay(NotificationKeysSaveDelay, session);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref _notificationKeysSaveScheduled, 0);
            return;
        }

        await _notificationKeysLock.WaitAsync();
        try
        {
            Interlocked.Exchange(ref _notificationKeysSaveScheduled, 0);
            if (session.IsCancellationRequested || _notificationKeys is null || NotificationKeyStore is null)
                return;

            // Other tabs of the app share the store. Keys they added are kept,
            // and keys one of them deleted, as when logging out, or that
            // belong to another account signed in there, are not replaced.
            var stored = NotificationKeySet.Parse(await NotificationKeyStore.LoadAsync());
            if ((stored is null && _notificationKeysStored) ||
                (stored is not null && stored.UserId != _notificationKeys.UserId))
            {
                _notificationKeys = null;
                _notificationKeysStored = false;
                return;
            }

            if (stored is not null)
                _notificationKeys.MergeFrom(stored);

            await NotificationKeyStore.SaveAsync(_notificationKeys.Serialize());
            _notificationKeysStored = true;
        }
        finally
        {
            _notificationKeysLock.Release();
        }
    }

    /// <summary>
    /// Deletes the notification keys kept on this device, for example when
    /// logging out or after this device was removed from the account.
    /// </summary>
    public async Task ForgetNotificationKeysAsync()
    {
        if (NotificationKeyStore is null)
            return;

        await _notificationKeysLock.WaitAsync();
        try
        {
            _notificationKeys = null;
            _notificationKeysStored = false;
            await NotificationKeyStore.ClearAsync();
        }
        finally
        {
            _notificationKeysLock.Release();
        }
    }
}
