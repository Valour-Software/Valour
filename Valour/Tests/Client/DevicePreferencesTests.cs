using System.Text.Json;
using Valour.Client.Device;
using Valour.Client.Storage;

namespace Valour.Tests.Client;

public class DevicePreferencesTests
{
    [Fact]
    public async Task LoadPreferences_WithoutStoredChoice_DoesNotForceGpuLayers()
    {
        await DevicePreferences.LoadPreferences(new MemoryAppStorage());

        Assert.False(DevicePreferences.ForceGpuAcceleration);
        Assert.False(DevicePreferences.VillageHandheldControls);
    }

    [Fact]
    public async Task VillageHandheldControls_RoundTripsThroughDeviceStorage()
    {
        var storage = new MemoryAppStorage();

        await DevicePreferences.SetVillageHandheldControlsEnabled(true, storage);
        await DevicePreferences.LoadPreferences(storage);

        Assert.True(DevicePreferences.VillageHandheldControls);
        Assert.True(await storage.GetAsync<bool>(DevicePreferences.VillageHandheldControlsStorageKey));
    }

    private sealed class MemoryAppStorage : IAppStorage
    {
        private readonly Dictionary<string, string> _items = new();

        public Task<string?> GetStringAsync(string key) =>
            Task.FromResult(_items.GetValueOrDefault(key));

        public Task SetStringAsync(string key, string value)
        {
            _items[key] = value;
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(string key) => Task.FromResult(
            _items.TryGetValue(key, out var value)
                ? JsonSerializer.Deserialize<T>(value)
                : default);

        public Task SetAsync<T>(string key, T value) =>
            SetStringAsync(key, JsonSerializer.Serialize(value));

        public Task<bool> ContainsKeyAsync(string key) =>
            Task.FromResult(_items.ContainsKey(key));

        public Task RemoveAsync(string key)
        {
            _items.Remove(key);
            return Task.CompletedTask;
        }
    }
}
