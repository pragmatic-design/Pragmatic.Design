using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Storage.InMemory.Tests;

public class InMemoryStorageServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemoryStorage_ReturnsSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        var result = services.AddInMemoryStorage();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddInMemoryStorage_RegistersIFileStorageSingleton()
    {
        var provider = new ServiceCollection().AddInMemoryStorage().BuildServiceProvider();

        var storage = provider.GetService<IFileStorage>();

        storage.Should().BeOfType<InMemoryFileStorage>();
        provider.GetService<IFileStorage>().Should().BeSameAs(storage);
    }

    [Fact]
    public void AddInMemoryStorage_ResolvesSameInstanceForBothInterfaces()
    {
        var provider = new ServiceCollection().AddInMemoryStorage().BuildServiceProvider();

        var storage = provider.GetRequiredService<IFileStorage>();
        var infoProvider = provider.GetRequiredService<IFileInfoProvider>();

        infoProvider.Should().BeSameAs(storage);
    }

    [Fact]
    public async Task AddInMemoryStorage_InfoProviderSeesFilesWrittenThroughStorage()
    {
        var provider = new ServiceCollection().AddInMemoryStorage().BuildServiceProvider();
        var storage = provider.GetRequiredService<IFileStorage>();
        var infoProvider = provider.GetRequiredService<IFileInfoProvider>();

        var uri = await storage.SaveAsync(new MemoryStream([1, 2, 3, 4]), "data.bin", "docs");

        var info = await infoProvider.GetInfoAsync(uri);
        info.Should().NotBeNull();
        info!.SizeBytes.Should().Be(4);
    }
}
