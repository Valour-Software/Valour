using Valour.Server.Cdn.Storage;

namespace Valour.Tests.Cdn;

public class CdnStorageProviderTests
{
    [Fact]
    public void ResolveMode_UsesFilesystemWhenCdnConfigurationIsMissing()
    {
        Assert.Equal(CdnStorageMode.FileSystem, CdnStorageProvider.ResolveMode(null));
    }
}
