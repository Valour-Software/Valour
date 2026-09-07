using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Valour.Server.Services;

namespace Valour.Tests.Server;

public class SentryDataProtectionTests
{
    [Fact]
    public void WrappedKeyRing_CanBeReloadedByFreshServiceProvider()
    {
        var repository = new Repository();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Kek"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        ServiceProvider CreateServices()
        {
            var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration).AddSingleton<DataProtectionKekProvider>();
            services.AddDataProtection().SetApplicationName("Sentry key activation regression");
            services.AddOptions<KeyManagementOptions>().Configure<DataProtectionKekProvider>((options, kek) =>
            {
                options.XmlRepository = repository;
                options.XmlEncryptor = new KekXmlEncryptor(kek);
            });
            return services.BuildServiceProvider();
        }
        string ciphertext;
        using (var first = CreateServices())
            ciphertext = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("round trip");
        Assert.Contains(repository.GetAllElements(), x => x.Descendants("encryptedKey").Any());
        using var second = CreateServices();
        Assert.Equal("round trip", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(ciphertext));
    }

    private sealed class Repository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];
        public IReadOnlyCollection<XElement> GetAllElements() => _elements.Select(x => new XElement(x)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => _elements.Add(new XElement(element));
    }
}
