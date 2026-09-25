using System.Text.Json;
using Valour.Sdk.E2ee;

namespace Valour.Sdk.Services;

public partial class E2eeService
{
    /// <summary>
    /// The largest pin set an approving device sends. The server refuses
    /// larger ones; a device with more pins than this links without sending
    /// them.
    /// </summary>
    private const int MaxLinkPinsBytes = 2 * 1024 * 1024 - 1024;

    /// <summary>
    /// What an approving device has verified, sent to a new device during an
    /// authenticated link so the new device starts from the same checks
    /// instead of trusting whatever the server shows it first.
    /// </summary>
    private sealed class LinkPinSet
    {
        /// <summary>Key log pins by user ID, including verification marks and accepted epochs.</summary>
        public Dictionary<long, KeyPin> Users { get; set; }

        /// <summary>Access log pins, keyed as in the access-pins store entry.</summary>
        public Dictionary<string, AccessPin> Access { get; set; }

        /// <summary>The newest key generation of each channel, keyed "planetId/channelId".</summary>
        public Dictionary<string, int> Generations { get; set; }

        /// <summary>The other person in each direct chat, by channel ID.</summary>
        public Dictionary<long, long> DirectPeers { get; set; }
    }

    /// <summary>
    /// Collects this device's pins and seals them to a new device. Returns
    /// null when there are too many to send.
    /// </summary>
    private async Task<byte[]> ExportLinkPinsAsync(string sessionId, DevicePublicKeys newDevice)
    {
        var set = new LinkPinSet();

        await _pinLock.WaitAsync();
        try
        {
            _pins ??= await LoadPinsAsync();
            _accessPins ??= await LoadAccessPinsAsync();
            set.Users = _pins.ToDictionary(p => p.Key, p => CopyPin(p.Value));
            set.Access = _accessPins.ToDictionary(p => p.Key,
                p => new AccessPin { Count = p.Value.Count, HeadHash = p.Value.HeadHash });
        }
        finally
        {
            _pinLock.Release();
        }

        set.Generations = _generationPins.ToArray()
            .ToDictionary(p => $"{p.Key.PlanetId}/{p.Key.ChannelId}", p => p.Value);
        set.DirectPeers = _directPeers.ToArray().ToDictionary(p => p.Key, p => p.Value);

        var json = JsonSerializer.SerializeToUtf8Bytes(set);
        if (json.Length > MaxLinkPinsBytes)
        {
            LogWarning("This device has too many pins to send to the new device, so it starts without them.");
            return null;
        }

        return E2eeCrypto.Seal(newDevice.EncryptPublicKey, json,
            DeviceLinkVerification.PinsContext(_client.Me.Id, sessionId, newDevice.DeviceId));
    }

    /// <summary>
    /// Opens pins an approving device sealed to this device. Call only after
    /// the approval proof checked out.
    /// </summary>
    private LinkPinSet OpenLinkPins(byte[] sealedPins, string sessionId, DeviceKeyPair device)
    {
        if (sealedPins is not { Length: > 0 })
            return null;

        try
        {
            var json = E2eeCrypto.Open(device.EncryptPrivateKey, sealedPins,
                DeviceLinkVerification.PinsContext(_client.Me.Id, sessionId, device.DeviceId));
            return JsonSerializer.Deserialize<LinkPinSet>(json);
        }
        catch (Exception e) when (e is E2eeFormatException or E2eeVerificationException or JsonException or
                                      System.Security.Cryptography.CryptographicException)
        {
            LogWarning($"The pins from the approving device could not be read: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Adds pins from the approving device. Pins this device already has
    /// win, so an import never loosens a check this device made itself:
    /// a user this device pinned keeps its count, verification mark, and
    /// accepted epoch, and a direct chat keeps its pinned person. Generation
    /// pins only ever rise.
    /// </summary>
    private async Task ImportLinkPinsAsync(LinkPinSet set)
    {
        if (set is null)
            return;

        await _pinLock.WaitAsync();
        try
        {
            _pins ??= await LoadPinsAsync();

            // Pins another tab or process sharing the store saved count as
            // this device's own.
            var stored = await LoadPinsAsync();
            var usersAdded = false;
            foreach (var (userId, pin) in set.Users ?? [])
            {
                if (pin is null || pin.Count <= 0 || string.IsNullOrEmpty(pin.HeadHash) ||
                    _pins.ContainsKey(userId) || stored.ContainsKey(userId))
                    continue;

                _pins[userId] = CopyPin(pin);

                // A state loaded before the pin existed is checked again.
                _userStates.TryRemove(userId, out _);
                usersAdded = true;
            }

            if (usersAdded)
                await SavePinsAsync();

            _accessPins ??= await LoadAccessPinsAsync();
            var storedAccess = await LoadAccessPinsAsync();
            var accessAdded = false;
            foreach (var (key, pin) in set.Access ?? [])
            {
                if (pin is null || pin.Count <= 0 || string.IsNullOrEmpty(pin.HeadHash) ||
                    _accessPins.ContainsKey(key) || storedAccess.ContainsKey(key) ||
                    !TryParseAccessPinKey(key, out var scope, out var scopeId))
                    continue;

                _accessPins[key] = new AccessPin { Count = pin.Count, HeadHash = pin.HeadHash };
                _accessLogs.TryRemove((scope, scopeId), out _);
                accessAdded = true;
            }

            if (accessAdded)
                await SaveAccessPinsAsync();
        }
        finally
        {
            _pinLock.Release();
        }

        foreach (var (key, generation) in set.Generations ?? [])
        {
            var parts = key.Split('/');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var planetId) ||
                !long.TryParse(parts[1], out var channelId) || generation <= 0)
                continue;

            var storeKey = StoreKey(GenerationPinKey(planetId, channelId));
            var storedBytes = await Store.GetAsync(storeKey);
            var current = Math.Max(storedBytes is { Length: 4 } ? BitConverter.ToInt32(storedBytes) : 0,
                _generationPins.GetValueOrDefault((planetId, channelId)));
            if (generation <= current)
                continue;

            _generationPins.AddOrUpdate((planetId, channelId), generation, (_, value) => Math.Max(value, generation));
            await Store.SetAsync(storeKey, BitConverter.GetBytes(generation));
        }

        foreach (var (channelId, peerId) in set.DirectPeers ?? [])
        {
            if (_directPeers.ContainsKey(channelId) ||
                await Store.GetAsync(StoreKey(DirectPeerKey(channelId))) is { Length: 8 })
                continue;

            await Store.SetAsync(StoreKey(DirectPeerKey(channelId)), BitConverter.GetBytes(peerId));
            _directPeers.TryAdd(channelId, peerId);
        }
    }

    private static KeyPin CopyPin(KeyPin pin) => new()
    {
        Epoch = pin.Epoch,
        Count = pin.Count,
        HeadHash = pin.HeadHash,
        Verified = pin.Verified,
        VerifiedChangedAt = pin.VerifiedChangedAt,
        AcceptedEpoch = Math.Min(pin.AcceptedEpoch ?? pin.Epoch, pin.Epoch),
        AcceptedChangedAt = pin.AcceptedChangedAt
    };

    private static bool TryParseAccessPinKey(string key, out AccessLogScope scope, out long scopeId)
    {
        scope = default;
        scopeId = 0;
        var parts = key?.Split(':');
        if (parts is not { Length: 2 } || !byte.TryParse(parts[0], out var scopeValue) ||
            !Enum.IsDefined((AccessLogScope)scopeValue) || !long.TryParse(parts[1], out scopeId))
            return false;

        scope = (AccessLogScope)scopeValue;
        return AccessPinKey(scope, scopeId) == key;
    }
}
