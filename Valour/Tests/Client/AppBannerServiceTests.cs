using Valour.Client.Storage;
using Valour.Client.Utility;
using Valour.Shared.Models;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class AppBannerServiceTests
{
    [Fact]
    public void PlatformBannerHash_IsStableAcrossOuterWhitespace()
    {
        var first = PlatformBanner.ComputeHash(" Notice ", " Maintenance tonight ", PlatformBannerKind.Warning);
        var second = PlatformBanner.ComputeHash("Notice", "Maintenance tonight", PlatformBannerKind.Warning);

        Assert.Equal(first, second);
        Assert.NotEqual(first, PlatformBanner.ComputeHash("Notice", "Maintenance tonight", PlatformBannerKind.Critical));
    }

    [Fact]
    public async Task Dismissal_AppliesOnlyToTheMatchingBannerHash()
    {
        var storage = new MemoryAppStorage();
        var service = new AppBannerService(storage);
        var first = Banner("First");
        var second = Banner("Second");

        await service.SetPlatformBannerAsync(first);
        Assert.Same(first, service.PlatformBanner);
        await service.DismissPlatformBannerAsync();
        Assert.Null(service.PlatformBanner);

        await service.SetPlatformBannerAsync(first);
        Assert.Null(service.PlatformBanner);
        await service.SetPlatformBannerAsync(second);
        Assert.Same(second, service.PlatformBanner);
    }

    private static PlatformBanner Banner(string message) => new()
    {
        Title = "Notice",
        Message = message,
        Kind = PlatformBannerKind.Information,
        Hash = PlatformBanner.ComputeHash("Notice", message, PlatformBannerKind.Information)
    };

    private sealed class MemoryAppStorage : IAppStorage
    {
        private readonly Dictionary<string, string> _values = new();
        public Task<string> GetStringAsync(string key) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetStringAsync(string key, string value) { _values[key] = value; return Task.CompletedTask; }
        public Task<T> GetAsync<T>(string key) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value) { _values[key] = value?.ToString(); return Task.CompletedTask; }
        public Task<bool> ContainsKeyAsync(string key) => Task.FromResult(_values.ContainsKey(key));
        public Task RemoveAsync(string key) { _values.Remove(key); return Task.CompletedTask; }
    }
}

public class PlanetMemberAvatarTests
{
    [Fact]
    public void NativeMemberAvatar_UsesRequestedGeneratedSize_AndPreservesVersion()
    {
        var member = new PlanetMember(null)
        {
            MemberAvatar = "https://cdn.example/valour-public/memberavatars/10/20/256.webp?v=42"
        };

        Assert.Equal(
            "https://cdn.example/valour-public/memberavatars/10/20/64.webp?v=42",
            member.GetAvatar(AvatarFormat.Webp64));
        Assert.Equal(
            "https://cdn.example/valour-public/memberavatars/10/20/128.webp?v=42",
            member.GetAvatar(AvatarFormat.Gif128));
    }
}
